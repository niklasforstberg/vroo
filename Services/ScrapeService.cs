using Microsoft.EntityFrameworkCore;
using Vroo.Data;
using Vroo.Models;

namespace Vroo.Services;

// Scrapes every result page for one watched model and records what changed.
public class ScrapeService(VrooDbContext db, BytbilClient bytbil, ILogger<ScrapeService> logger)
{
    // Safety net against a pagination loop; at 24 per page this is ~2 400 listings.
    private const int MaxPages = 100;

    public static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    public async Task RunAsync(int watchedModelId, string trigger, CancellationToken ct)
    {
        var watched = await db.WatchedModels.FindAsync([watchedModelId], ct);
        if (watched is null) return;

        var run = new ScrapeRun { WatchedModelId = watched.Id, Trigger = trigger, StartedAt = DateTime.UtcNow };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Scraping {Make} {Model} (run {RunId})", watched.Make, watched.Model, run.Id);

        var seen = new HashSet<int>();
        var today = StockholmDate(run.StartedAt);
        // A second run on the same day must not try to insert the same sightings again.
        var sightedToday = (await db.Sightings
                .Where(s => s.Date == today && s.Car.WatchedModelId == watched.Id)
                .Select(s => s.CarId).ToListAsync(ct))
            .ToHashSet();
        try
        {
            for (var page = 1; page <= MaxPages; page++)
            {
                var listings = await bytbil.GetSearchPageAsync(watched.Make, watched.Model, page, ct);
                if (listings is null)
                {
                    run.Error = $"Kunde inte hämta resultatsida {page}";
                    break;
                }
                if (listings.Count == 0) break;

                foreach (var listing in listings)
                    await StoreAsync(watched.Id, listing, run, seen, today, sightedToday, ct);

                await db.SaveChangesAsync(ct);
            }

            // Partial runs still record their sightings (they are true), but only a
            // complete run may set the day's market snapshot.
            if (run.Error is null)
            {
                await WriteDailyStatAsync(db, watched.Id, today, ct);
                await db.SaveChangesAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            run.Error = "Avbruten";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scrape run {RunId} failed", run.Id);
            run.Error = ex.Message;
        }

        run.CarsSeen = seen.Count;
        run.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Run {RunId} done: {Seen} seen, {New} new, {Changes} price changes, error: {Error}",
            run.Id, run.CarsSeen, run.NewCars, run.PriceChanges, run.Error ?? "none");
    }

    private async Task StoreAsync(int watchedModelId, ListingSummary listing, ScrapeRun run, HashSet<int> seen,
        DateOnly today, HashSet<int> sightedToday, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var car = await db.Cars.FirstOrDefaultAsync(c => c.Url == listing.Url, ct);

        if (car is null)
        {
            // Unknown URL: fetch the listing page for registration number and specs.
            var details = await bytbil.GetDetailsAsync(listing.Url, ct);

            // Same car relisted under a new URL?
            if (details?.RegistrationNumber is { } regnr)
            {
                car = await db.Cars.FirstOrDefaultAsync(
                    c => c.WatchedModelId == watchedModelId && c.RegistrationNumber == regnr, ct);
                if (car is not null) car.Url = listing.Url;
            }

            if (car is null)
            {
                car = new Car
                {
                    WatchedModelId = watchedModelId,
                    Url = listing.Url,
                    Title = listing.Title,
                    Price = listing.Price,
                    FirstSeenAt = now,
                    RegistrationNumber = details?.RegistrationNumber,
                    Color = details?.Color,
                    DriveType = details?.DriveType,
                    Gearbox = details?.Gearbox,
                    BodyType = details?.BodyType,
                };
                car.Prices.Add(new CarPrice { Price = listing.Price, ObservedAt = now });
                db.Cars.Add(car);
                run.NewCars++;
            }
        }

        if (car.Id != 0 && !seen.Add(car.Id)) return; // listed twice as pages shifted

        if (car.Id != 0 && car.Price != listing.Price)
        {
            db.CarPrices.Add(new CarPrice { CarId = car.Id, Price = listing.Price, ObservedAt = now });
            run.PriceChanges++;
        }

        car.Title = listing.Title;
        car.Price = listing.Price;
        car.Year = listing.Year;
        car.MileageMil = listing.MileageMil;
        car.Location = listing.Location;
        car.ImageUrl = listing.ImageUrl ?? car.ImageUrl;
        car.LastSeenAt = now;

        if (car.Id == 0)
        {
            await db.SaveChangesAsync(ct); // get an Id so the seen-set can track it
            seen.Add(car.Id);
        }

        if (sightedToday.Add(car.Id))
            db.Sightings.Add(new Sighting { CarId = car.Id, Date = today });
    }

    /// <summary>
    /// (Re)computes the day's snapshot from Sightings, with each car's price as of the end of
    /// that day taken from CarPrices. Adds or updates the DailyStat; the caller saves.
    /// Mileage is the car's current value; mileage has no history.
    /// </summary>
    public static async Task WriteDailyStatAsync(VrooDbContext db, int watchedModelId, DateOnly date, CancellationToken ct)
    {
        var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), Stockholm);
        var rows = await db.Sightings
            .Where(s => s.Date == date && s.Car.WatchedModelId == watchedModelId)
            .Select(s => new
            {
                Price = s.Car.Prices
                    .Where(p => p.ObservedAt < dayEndUtc)
                    .OrderByDescending(p => p.ObservedAt)
                    .Select(p => (int?)p.Price)
                    .FirstOrDefault(),
                s.Car.MileageMil,
            })
            .ToListAsync(ct);
        if (rows.Count == 0) return; // misspelt model or nothing for sale; a 0-median point would wreck the chart

        var stat = await db.DailyStats.FindAsync([watchedModelId, date], ct);
        if (stat is null)
        {
            stat = new DailyStat { WatchedModelId = watchedModelId, Date = date };
            db.DailyStats.Add(stat);
        }

        stat.Listings = rows.Count;
        stat.MedianPrice = Median(rows.Where(r => r.Price.HasValue).Select(r => r.Price!.Value)) ?? 0;
        stat.MedianMileageMil = Median(rows.Where(r => r.MileageMil.HasValue).Select(r => r.MileageMil!.Value));
    }

    public static DateOnly StockholmDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Stockholm));

    public static int? Median(IEnumerable<int> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return null;
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (int)(((long)sorted[mid - 1] + sorted[mid]) / 2);
    }
}
