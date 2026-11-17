using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vroo.Data;
using Vroo.Models.DTOs;
using Vroo.Services;

namespace Vroo.Endpoints;

// Admin only: users, all watched models, scrape runs.
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api").RequireAuthorization("admin");

        admin.MapGet("/runs", async (VrooDbContext db, int? limit, CancellationToken ct) =>
            await db.ScrapeRuns.OrderByDescending(r => r.StartedAt).Take(Math.Clamp(limit ?? 100, 1, 500))
                .Select(r => new ScrapeRunResponse(r.Id, r.WatchedModelId, r.WatchedModel.Make, r.WatchedModel.Model,
                    r.Trigger, r.StartedAt, r.FinishedAt, r.CarsSeen, r.NewCars, r.PriceChanges, r.Error))
                .ToListAsync(ct));

        admin.MapPost("/models/run-all", async (ScrapeScheduler scheduler, CancellationToken ct) =>
        {
            await scheduler.EnqueueAllActiveAsync("manual", ct);
            return Results.Accepted();
        });

        admin.MapGet("/admin/users", async (VrooDbContext db, CancellationToken ct) =>
            await db.Users.OrderBy(u => u.CreatedAt)
                .Select(u => new AdminUserResponse(u.Id, u.Email, u.IsAdmin, u.CreatedAt, u.LastSeenAt,
                    u.EmailVerifiedAt != null, u.Subscriptions.Count))
                .ToListAsync(ct));

        admin.MapDelete("/admin/users/{id:int}", async (int id, ClaimsPrincipal user, VrooDbContext db, CancellationToken ct) =>
        {
            if (id == user.UserId()) return Results.BadRequest(new { error = "Du kan inte ta bort ditt eget konto." });
            var deleted = await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });

        admin.MapGet("/admin/models", async (VrooDbContext db, CancellationToken ct) =>
            await db.WatchedModels.OrderBy(w => w.Make).ThenBy(w => w.Model)
                .Select(w => new AdminModelResponse(w.Id, w.Make, w.Model,
                    w.Subscriptions.Count, w.Subscriptions.Count(s => s.IsActive), w.Cars.Count))
                .ToListAsync(ct));

        // Deletes the model with all its history, for every follower.
        admin.MapDelete("/admin/models/{id:int}", async (int id, VrooDbContext db, ScrapeScheduler scheduler, CancellationToken ct) =>
        {
            if (scheduler.CurrentModelId == id)
                return Results.Conflict(new { error = "Modellen hämtas just nu. Försök igen när körningen är klar." });
            var deleted = await db.WatchedModels.Where(w => w.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }
}
