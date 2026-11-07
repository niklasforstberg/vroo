using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vroo.Data;
using Vroo.Models;

namespace Vroo.Services;

// One-off import of usedcarprices/cars.db (the old Python scraper). Run with:
//   dotnet Vroo.dll import /path/to/cars.db
// Imported models start paused, so nothing is scraped until you switch them on in the UI.
public static partial class SqliteImporter
{
    public static async Task RunAsync(VrooDbContext db, string path, ILogger logger)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("SQLite file not found", path);
        if (await db.Cars.AnyAsync())
            throw new InvalidOperationException("The database already contains cars; import only runs into an empty database.");

        await using var sqlite = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await sqlite.OpenAsync();

        // --- cars -----------------------------------------------------------
        var oldCars = new List<OldCar>();
        await using (var cmd = sqlite.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, title, make, model, year, mileage, location, price, registration_number,
                       color, drive_type, gearbox, bodytype, first_seen, last_seen, url
                FROM cars
                """;
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var price = BytbilClient.ParseInt(Str(r, 7) ?? "");
                var url = Str(r, 15);
                if (price is null || url is null) continue;
                oldCars.Add(new OldCar(
                    r.GetInt64(0), Str(r, 1) ?? "", Str(r, 2) ?? "", Str(r, 3) ?? "",
                    BytbilClient.ParseInt(Str(r, 4) ?? ""), BytbilClient.ParseInt(Str(r, 5) ?? ""),
                    Str(r, 6) is "N/A" ? null : Str(r, 6), price.Value,
                    Str(r, 8) is null or "-" or "N/A" ? null : Str(r, 8),
                    Str(r, 9), Str(r, 10), Str(r, 11), Str(r, 12),
                    ToUtc(Str(r, 13)!), ToUtc(Str(r, 14)!), url));
            }
        }

        // price_history stores the OLD price at the moment it changed.
        var history = new Dictionary<long, List<(int Price, DateTime At)>>();
        await using (var cmd = sqlite.CreateCommand())
        {
            cmd.CommandText = "SELECT car_id, price, timestamp FROM price_history ORDER BY timestamp";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var price = BytbilClient.ParseInt(Str(r, 1) ?? "");
                if (price is null) continue;
                var carId = r.GetInt64(0);
                if (!history.TryGetValue(carId, out var list)) history[carId] = list = [];
                list.Add((price.Value, ToUtc(Str(r, 2)!)));
            }
        }

        // --- watched models ---------------------------------------------------
        var models = new Dictionary<(string, string), WatchedModel>();
        foreach (var key in oldCars.Select(c => (c.Make, c.Model)).Distinct())
        {
            var wm = await db.WatchedModels.FirstOrDefaultAsync(w => w.Make == key.Make && w.Model == key.Model)
                ?? db.WatchedModels.Add(new WatchedModel
                {
                    Make = key.Make, Model = key.Model, IsActive = false, CreatedAt = DateTime.UtcNow,
                }).Entity;
            models[key] = wm;
        }

        // --- cars + reconstructed price series ----------------------------------
        foreach (var o in oldCars)
        {
            var car = new Car
            {
                WatchedModel = models[(o.Make, o.Model)],
                Url = o.Url, Title = o.Title, RegistrationNumber = o.Regnr, Year = o.Year,
                MileageMil = o.Mileage, Location = o.Location, Price = o.Price,
                Color = o.Color, DriveType = o.DriveType, Gearbox = o.Gearbox, BodyType = o.BodyType,
                FirstSeenAt = o.FirstSeen, LastSeenAt = o.LastSeen,
            };
            car.Prices.AddRange(PriceSeries(o, history.GetValueOrDefault(o.Id) ?? []));
            db.Cars.Add(car);
        }
        await db.SaveChangesAsync();

        // --- runs + daily stats -------------------------------------------------
        var runs = new List<(WatchedModel Model, DateTime At, int Found)>();
        await using (var cmd = sqlite.CreateCommand())
        {
            cmd.CommandText = "SELECT timestamp, cars_found, search_params FROM scraping_logs ORDER BY timestamp";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var p = Str(r, 2) ?? "";
                var make = MakeRegex().Match(p);
                var model = ModelRegex().Match(p);
                if (!make.Success || !model.Success) continue; // early test runs searched a list of models
                if (!models.TryGetValue((make.Groups[1].Value, model.Groups[1].Value), out var wm)) continue;
                var at = ToUtc(Str(r, 0)!);
                var found = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                runs.Add((wm, at, found));
                db.ScrapeRuns.Add(new ScrapeRun
                {
                    WatchedModel = wm, Trigger = "import", StartedAt = at, FinishedAt = at, CarsSeen = found,
                });
            }
        }

        // A car counts as listed at a run if it was first seen before the run ended
        // (approximated as a day later) and last seen after it started.
        var carsByModel = (await db.Cars.Include(c => c.Prices).AsNoTracking().ToListAsync())
            .ToLookup(c => c.WatchedModelId);
        foreach (var day in runs.Where(r => r.Found > 0)
                     .GroupBy(r => (r.Model, Date: DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(r.At, ScrapeService.Stockholm)))))
        {
            var at = day.Max(r => r.At);
            var listed = carsByModel[day.Key.Model.Id]
                .Where(c => c.FirstSeenAt <= at.AddDays(1) && c.LastSeenAt >= at)
                .ToList();
            if (listed.Count == 0) continue;
            db.DailyStats.Add(new DailyStat
            {
                WatchedModelId = day.Key.Model.Id,
                Date = day.Key.Date,
                Listings = listed.Count,
                MedianPrice = ScrapeService.Median(listed.Select(c => PriceAt(c, at.AddDays(1))))!.Value,
                MedianMileageMil = ScrapeService.Median(listed.Where(c => c.MileageMil.HasValue).Select(c => c.MileageMil!.Value)),
            });
        }
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Imported {Cars} cars, {Prices} price points, {Runs} runs, {Models} watched models (paused)",
            oldCars.Count, await db.CarPrices.CountAsync(), runs.Count, models.Count);
    }

    // Old rows hold the price that was replaced at each change. Rebuild the series:
    // first price at first_seen, then each change's new price at the change time.
    private static IEnumerable<CarPrice> PriceSeries(OldCar car, List<(int Price, DateTime At)> changes)
    {
        if (changes.Count == 0)
        {
            yield return new CarPrice { Price = car.Price, ObservedAt = car.FirstSeen };
            yield break;
        }
        yield return new CarPrice { Price = changes[0].Price, ObservedAt = car.FirstSeen };
        for (var i = 0; i < changes.Count; i++)
        {
            var next = i + 1 < changes.Count ? changes[i + 1].Price : car.Price;
            yield return new CarPrice { Price = next, ObservedAt = changes[i].At };
        }
    }

    private static int PriceAt(Car car, DateTime at) =>
        car.Prices.Where(p => p.ObservedAt <= at).OrderBy(p => p.ObservedAt).LastOrDefault()?.Price ?? car.Price;

    // The Python script stored naive local timestamps (datetime.now() on a Mac in Sweden).
    private static DateTime ToUtc(string local)
    {
        var t = DateTime.Parse(local, CultureInfo.InvariantCulture, DateTimeStyles.None);
        if (ScrapeService.Stockholm.IsInvalidTime(t)) t = t.AddHours(1); // inside the spring-forward gap
        return TimeZoneInfo.ConvertTimeToUtc(t, ScrapeService.Stockholm);
    }

    private static string? Str(SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } s ? s : null;

    private record OldCar(
        long Id, string Title, string Make, string Model, int? Year, int? Mileage, string? Location, int Price,
        string? Regnr, string? Color, string? DriveType, string? Gearbox, string? BodyType,
        DateTime FirstSeen, DateTime LastSeen, string Url);

    [GeneratedRegex(@"'Makes': '([^']*)'")]
    private static partial Regex MakeRegex();

    [GeneratedRegex(@"'Models': '([^']*)'")]
    private static partial Regex ModelRegex();
}
