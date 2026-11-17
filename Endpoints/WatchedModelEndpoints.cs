using System.Security.Claims;
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

        // Anonymous: <img> can't send the bearer token, and the picture is public on bytbil anyway.
        // It never changes once stored, so let the browser keep it.
        app.MapGet("/api/models/{id:int}/image", async (int id, VrooDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var img = await db.ModelImages.FindAsync([id], ct);
            if (img is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "public, max-age=604800";
            return Results.File(img.Data, img.ContentType);
        });

        // The models the current user follows.
        api.MapGet("/models", async (ClaimsPrincipal user, VrooDbContext db, ScrapeScheduler scheduler, CancellationToken ct) =>
        {
            var userId = user.UserId();
            var subs = await db.Subscriptions.Where(s => s.UserId == userId).Include(s => s.WatchedModel)
                .OrderBy(s => s.WatchedModel.Make).ThenBy(s => s.WatchedModel.Model).ToListAsync(ct);
            var ids = subs.Select(s => s.WatchedModelId).ToList();
            var stats = (await db.DailyStats.Where(s => ids.Contains(s.WatchedModelId)).ToListAsync(ct)).ToLookup(s => s.WatchedModelId);
            var lastRuns = await LastRunsAsync(db, ct);
            var withImage = (await db.ModelImages.Select(i => i.WatchedModelId).ToListAsync(ct)).ToHashSet();
            return subs.Select(s => ToResponse(s.WatchedModel, s.IsActive, stats[s.WatchedModelId],
                lastRuns.GetValueOrDefault(s.WatchedModelId), withImage.Contains(s.WatchedModelId), scheduler)).ToList();
        });

        // Every make/model someone watches, as suggestions for the add form.
        api.MapGet("/catalog", async (VrooDbContext db, CancellationToken ct) =>
            await db.WatchedModels.OrderBy(w => w.Make).ThenBy(w => w.Model)
                .Select(w => new CatalogEntry(w.Make, w.Model)).ToListAsync(ct));

        api.MapGet("/models/{id:int}", async (int id, ClaimsPrincipal user, VrooDbContext db, ScrapeScheduler scheduler, CancellationToken ct) =>
        {
            if (!await CanSeeAsync(db, user, id, ct)) return Results.NotFound();
            var w = await db.WatchedModels.FindAsync([id], ct);
            if (w is null) return Results.NotFound();
            var userId = user.UserId();
            var isActive = await db.Subscriptions.AnyAsync(s => s.UserId == userId && s.WatchedModelId == id && s.IsActive, ct);
            var stats = await db.DailyStats.Where(s => s.WatchedModelId == id).OrderBy(s => s.Date).ToListAsync(ct);
            var lastRun = (await LastRunsAsync(db, ct, id)).GetValueOrDefault(id);
            var hasImage = await db.ModelImages.AnyAsync(i => i.WatchedModelId == id, ct);
            return Results.Ok(new WatchedModelDetailResponse(
                ToResponse(w, isActive, stats, lastRun, hasImage, scheduler),
                stats.Select(s => new DailyStatResponse(s.Date, s.Listings, s.MedianPrice, s.MedianMileageMil)).ToList()));
        });

        // Follow a make/model: reuses the watched model if someone already has it, otherwise creates it.
        api.MapPost("/models", async (WatchedModelCreateRequest req, ClaimsPrincipal user, VrooDbContext db,
            ScrapeScheduler scheduler, IConfiguration config, CancellationToken ct) =>
        {
            var make = req.Make?.Trim() ?? "";
            var model = req.Model?.Trim() ?? "";
            if (make.Length is 0 or > 100 || model.Length is 0 or > 100)
                return Results.BadRequest(new { error = "Ange både märke och modell." });

            var userId = user.UserId();
            var limit = config.GetValue("Limits:ModelsPerUser", 10);
            if (!user.IsInRole("admin") && await db.Subscriptions.CountAsync(s => s.UserId == userId, ct) >= limit)
                return Results.BadRequest(new { error = $"Du kan bevaka högst {limit} modeller. Ta bort en för att lägga till en ny." });

            var w = await db.WatchedModels.FirstOrDefaultAsync(w => w.Make.ToLower() == make.ToLower() && w.Model.ToLower() == model.ToLower(), ct);
            if (w is not null && await db.Subscriptions.AnyAsync(s => s.UserId == userId && s.WatchedModelId == w.Id, ct))
                return Results.Conflict(new { error = $"Du bevakar redan {w.Make} {w.Model}." });

            var isNew = w is null;
            w ??= db.WatchedModels.Add(new WatchedModel { Make = make, Model = model, CreatedAt = DateTime.UtcNow }).Entity;
            db.Subscriptions.Add(new Subscription { UserId = userId, WatchedModel = w, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            if (isNew) scheduler.Enqueue(w.Id, "manual"); // fetch right away so the page isn't empty until tomorrow

            return Results.Created($"/api/models/{w.Id}", new { w.Id });
        });

        // Pause or resume the current user's subscription.
        api.MapPatch("/models/{id:int}", async (int id, WatchedModelUpdateRequest req, ClaimsPrincipal user, VrooDbContext db, CancellationToken ct) =>
        {
            var sub = await db.Subscriptions.FindAsync([user.UserId(), id], ct);
            if (sub is null) return Results.NotFound();
            sub.IsActive = req.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Unfollow. The model and its history stay; the admin can delete it.
        api.MapDelete("/models/{id:int}", async (int id, ClaimsPrincipal user, VrooDbContext db, CancellationToken ct) =>
        {
            var userId = user.UserId();
            var deleted = await db.Subscriptions.Where(s => s.UserId == userId && s.WatchedModelId == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });

        api.MapPost("/models/{id:int}/run", async (int id, ClaimsPrincipal user, VrooDbContext db, ScrapeScheduler scheduler, CancellationToken ct) =>
        {
            if (!await CanSeeAsync(db, user, id, ct) || !await db.WatchedModels.AnyAsync(w => w.Id == id, ct))
                return Results.NotFound();
            scheduler.Enqueue(id, "manual");
            return Results.Accepted();
        });

        api.MapGet("/models/{id:int}/cars", async (int id, ClaimsPrincipal user, VrooDbContext db, CancellationToken ct) =>
        {
            if (!await CanSeeAsync(db, user, id, ct)) return Results.NotFound();
            var listedSince = await ListedSinceAsync(db, id, ct);
            var floor = await PriceFloorAsync(db, id, ct);
            var cars = await CarQuery(db.Cars.Where(c => c.WatchedModelId == id)
                    .OrderByDescending(c => c.LastSeenAt).ThenByDescending(c => c.FirstSeenAt), listedSince, floor)
                .ToListAsync(ct);
            return Results.Ok(cars);
        });

        api.MapGet("/cars/{id:int}", async (int id, ClaimsPrincipal user, VrooDbContext db, CancellationToken ct) =>
        {
            var c = await db.Cars.Include(c => c.Prices).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (c is null || !await CanSeeAsync(db, user, c.WatchedModelId, ct)) return Results.NotFound();
            var listedSince = await ListedSinceAsync(db, c.WatchedModelId, ct);
            var floor = await PriceFloorAsync(db, c.WatchedModelId, ct);
            var car = await CarQuery(db.Cars.Where(x => x.Id == id), listedSince, floor).FirstAsync(ct);
            return Results.Ok(new CarDetailResponse(car, c.Color, c.BodyType,
                c.Prices.OrderBy(p => p.ObservedAt).Select(p => new CarPriceResponse(p.Price, p.ObservedAt)).ToList()));
        });
    }

    // Users see the models they follow; the admin sees all.
    private static async Task<bool> CanSeeAsync(VrooDbContext db, ClaimsPrincipal user, int watchedModelId, CancellationToken ct)
    {
        if (user.IsInRole("admin")) return true;
        var userId = user.UserId();
        return await db.Subscriptions.AnyAsync(s => s.UserId == userId && s.WatchedModelId == watchedModelId, ct);
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
        WatchedModel w, bool isActive, IEnumerable<DailyStat> stats, ScrapeRunResponse? lastRun, bool hasImage, ScrapeScheduler scheduler)
    {
        var ordered = stats.OrderBy(s => s.Date).ToList();
        var latest = ordered.LastOrDefault();
        // Only compare with a point 30–45 days back; across a longer gap the "30 days" label would lie.
        var monthAgo = latest is null ? null : ordered.LastOrDefault(s =>
            s.Date <= latest.Date.AddDays(-30) && s.Date >= latest.Date.AddDays(-45));
        var status = scheduler.CurrentModelId == w.Id ? "running"
            : scheduler.Pending.Contains(w.Id) ? "queued" : "idle";

        return new WatchedModelResponse(w.Id, w.Make, w.Model, isActive, hasImage, status,
            latest?.Listings, latest?.MedianPrice, latest?.Date, monthAgo?.MedianPrice, lastRun);
    }
}
