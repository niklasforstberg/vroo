using Microsoft.EntityFrameworkCore;
using Vroo.Data;
using Vroo.Models;
using Vroo.Models.DTOs;
using Vroo.Services;

namespace Vroo.Endpoints;

public static class WatchedModelEndpoints
{
    public static void MapWatchedModelEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/models", async (VrooDbContext db, ScrapeScheduler scheduler, CancellationToken ct) =>
        {
            var models = await db.WatchedModels.OrderBy(w => w.Make).ThenBy(w => w.Model).ToListAsync(ct);
            var stats = (await db.DailyStats.ToListAsync(ct)).ToLookup(s => s.WatchedModelId);
            var lastRuns = await LastRunsAsync(db, ct);
            return models.Select(w => ToResponse(w, stats[w.Id], lastRuns.GetValueOrDefault(w.Id), scheduler)).ToList();
        });

        api.MapGet("/models/{id:int}", async (int id, VrooDbContext db, ScrapeScheduler scheduler, CancellationToken ct) =>
        {
            var w = await db.WatchedModels.FindAsync([id], ct);
            if (w is null) return Results.NotFound();
            var stats = await db.DailyStats.Where(s => s.WatchedModelId == id).OrderBy(s => s.Date).ToListAsync(ct);
            var lastRun = (await LastRunsAsync(db, ct, id)).GetValueOrDefault(id);
            return Results.Ok(new WatchedModelDetailResponse(
                ToResponse(w, stats, lastRun, scheduler),
                stats.Select(s => new DailyStatResponse(s.Date, s.Listings, s.MedianPrice, s.MedianMileageMil)).ToList()));
        });

        api.MapPost("/models", async (WatchedModelCreateRequest req, VrooDbContext db, ScrapeScheduler scheduler, CancellationToken ct) =>
        {
            var make = req.Make?.Trim() ?? "";
            var model = req.Model?.Trim() ?? "";
            if (make.Length is 0 or > 100 || model.Length is 0 or > 100)
                return Results.BadRequest(new { error = "Ange både märke och modell." });

            if (await db.WatchedModels.AnyAsync(w => w.Make.ToLower() == make.ToLower() && w.Model.ToLower() == model.ToLower(), ct))
                return Results.Conflict(new { error = $"{make} {model} bevakas redan." });

            var w = new WatchedModel { Make = make, Model = model, CreatedAt = DateTime.UtcNow };
            db.WatchedModels.Add(w);
            await db.SaveChangesAsync(ct);
            scheduler.Enqueue(w.Id, "manual"); // fetch right away so the page isn't empty until tomorrow

            return Results.Created($"/api/models/{w.Id}", ToResponse(w, [], null, scheduler));
        });

        api.MapPatch("/models/{id:int}", async (int id, WatchedModelUpdateRequest req, VrooDbContext db, CancellationToken ct) =>
        {
            var w = await db.WatchedModels.FindAsync([id], ct);
            if (w is null) return Results.NotFound();
            w.IsActive = req.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapDelete("/models/{id:int}", async (int id, VrooDbContext db, ScrapeScheduler scheduler, CancellationToken ct) =>
        {
            if (scheduler.CurrentModelId == id)
                return Results.Conflict(new { error = "Modellen hämtas just nu. Försök igen när körningen är klar." });
            var deleted = await db.WatchedModels.Where(w => w.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });

        api.MapPost("/models/{id:int}/run", async (int id, VrooDbContext db, ScrapeScheduler scheduler, CancellationToken ct) =>
        {
            if (!await db.WatchedModels.AnyAsync(w => w.Id == id, ct)) return Results.NotFound();
            scheduler.Enqueue(id, "manual");
            return Results.Accepted();
        });

        api.MapPost("/models/run-all", async (ScrapeScheduler scheduler, CancellationToken ct) =>
        {
            await scheduler.EnqueueAllActiveAsync("manual", ct);
            return Results.Accepted();
        });

        api.MapGet("/models/{id:int}/cars", async (int id, VrooDbContext db, CancellationToken ct) =>
        {
            var listedSince = await ListedSinceAsync(db, id, ct);
            var floor = await PriceFloorAsync(db, id, ct);
            var cars = await CarQuery(db.Cars.Where(c => c.WatchedModelId == id)
                    .OrderByDescending(c => c.LastSeenAt).ThenByDescending(c => c.FirstSeenAt), listedSince, floor)
                .ToListAsync(ct);
            return Results.Ok(cars);
        });

        api.MapGet("/cars/{id:int}", async (int id, VrooDbContext db, CancellationToken ct) =>
        {
            var c = await db.Cars.Include(c => c.Prices).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (c is null) return Results.NotFound();
            var listedSince = await ListedSinceAsync(db, c.WatchedModelId, ct);
            var floor = await PriceFloorAsync(db, c.WatchedModelId, ct);
            var car = await CarQuery(db.Cars.Where(x => x.Id == id), listedSince, floor).FirstAsync(ct);
            return Results.Ok(new CarDetailResponse(car, c.Color, c.BodyType,
                c.Prices.OrderBy(p => p.ObservedAt).Select(p => new CarPriceResponse(p.Price, p.ObservedAt)).ToList()));
        });

        api.MapGet("/runs", async (VrooDbContext db, int? limit, CancellationToken ct) =>
            await db.ScrapeRuns.OrderByDescending(r => r.StartedAt).Take(Math.Clamp(limit ?? 100, 1, 500))
                .Select(r => new ScrapeRunResponse(r.Id, r.WatchedModelId, r.WatchedModel.Make, r.WatchedModel.Model,
                    r.Trigger, r.StartedAt, r.FinishedAt, r.CarsSeen, r.NewCars, r.PriceChanges, r.Error))
                .ToListAsync(ct));
    }

    // Cars seen at or after the start of the model's latest successful run are still for sale.
    private static Task<DateTime?> ListedSinceAsync(VrooDbContext db, int watchedModelId, CancellationToken ct) =>
        db.ScrapeRuns
            .Where(r => r.WatchedModelId == watchedModelId && r.FinishedAt != null && r.Error == null)
            .MaxAsync(r => (DateTime?)r.StartedAt, ct);

    // Same floor as the stats, from the model's latest median.
    private static async Task<int> PriceFloorAsync(VrooDbContext db, int watchedModelId, CancellationToken ct) =>
        ScrapeService.PriceFloor(await db.DailyStats.Where(s => s.WatchedModelId == watchedModelId)
            .OrderByDescending(s => s.Date).Select(s => (int?)s.MedianPrice).FirstOrDefaultAsync(ct));

    private static IQueryable<CarResponse> CarQuery(IQueryable<Car> cars, DateTime? listedSince, int priceFloor) =>
        cars.Select(c => new CarResponse(
            c.Id, c.Title, c.Url, c.ImageUrl, c.RegistrationNumber,
            c.Year, c.MileageMil, c.Location, c.Gearbox, c.DriveType,
            c.Price,
            c.Prices.OrderBy(p => p.ObservedAt).Select(p => p.Price).FirstOrDefault(),
            c.Prices.Count() - 1,
            c.FirstSeenAt, c.LastSeenAt,
            listedSince != null && c.LastSeenAt >= listedSince,
            c.Price >= priceFloor));

    private static async Task<Dictionary<int, ScrapeRunResponse>> LastRunsAsync(VrooDbContext db, CancellationToken ct, int? onlyId = null)
    {
        var runs = db.ScrapeRuns.AsQueryable();
        if (onlyId is not null) runs = runs.Where(r => r.WatchedModelId == onlyId);
        var latestIds = runs.GroupBy(r => r.WatchedModelId).Select(g => g.Max(r => r.Id));
        return await db.ScrapeRuns.Where(r => latestIds.Contains(r.Id))
            .Select(r => new ScrapeRunResponse(r.Id, r.WatchedModelId, r.WatchedModel.Make, r.WatchedModel.Model,
                r.Trigger, r.StartedAt, r.FinishedAt, r.CarsSeen, r.NewCars, r.PriceChanges, r.Error))
            .ToDictionaryAsync(r => r.WatchedModelId, ct);
    }

    private static WatchedModelResponse ToResponse(
        WatchedModel w, IEnumerable<DailyStat> stats, ScrapeRunResponse? lastRun, ScrapeScheduler scheduler)
    {
        var ordered = stats.OrderBy(s => s.Date).ToList();
        var latest = ordered.LastOrDefault();
        // Only compare with a point 30–45 days back; across a longer gap the "30 days" label would lie.
        var monthAgo = latest is null ? null : ordered.LastOrDefault(s =>
            s.Date <= latest.Date.AddDays(-30) && s.Date >= latest.Date.AddDays(-45));
        var status = scheduler.CurrentModelId == w.Id ? "running"
            : scheduler.Pending.Contains(w.Id) ? "queued" : "idle";

        return new WatchedModelResponse(w.Id, w.Make, w.Model, w.IsActive, status,
            latest?.Listings, latest?.MedianPrice, latest?.Date, monthAgo?.MedianPrice, lastRun);
    }
}
