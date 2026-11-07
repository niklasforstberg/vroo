using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Vroo.Data;

namespace Vroo.Services;

// Runs all active watched models once a day (Scrape:DailyAt, Stockholm time) and
// executes manual requests from the UI. Everything goes through one queue so bytbil
// never sees two scrapes at once.
public class ScrapeScheduler(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<ScrapeScheduler> logger) : BackgroundService
{
    private readonly Channel<(int WatchedModelId, string Trigger)> _queue =
        Channel.CreateUnbounded<(int, string)>();
    private readonly HashSet<int> _pending = [];
    private readonly object _lock = new();

    /// <summary>The model being scraped right now, if any.</summary>
    public int? CurrentModelId { get; private set; }

    public IReadOnlyCollection<int> Pending
    {
        get { lock (_lock) return _pending.ToArray(); }
    }

    /// <summary>Queues a model unless it is already waiting. Returns false if it was.</summary>
    public bool Enqueue(int watchedModelId, string trigger)
    {
        lock (_lock)
        {
            if (CurrentModelId == watchedModelId || !_pending.Add(watchedModelId)) return false;
        }
        _queue.Writer.TryWrite((watchedModelId, trigger));
        return true;
    }

    public async Task EnqueueAllActiveAsync(string trigger, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VrooDbContext>();
        var ids = await db.WatchedModels.Where(w => w.IsActive).OrderBy(w => w.Id).Select(w => w.Id).ToListAsync(ct);
        foreach (var id in ids) Enqueue(id, trigger);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (config.GetValue("Scrape:Enabled", true))
            _ = ScheduleLoopAsync(ct);
        else
            logger.LogInformation("Daily scrape disabled (Scrape:Enabled=false); manual runs still work");

        await foreach (var (id, trigger) in _queue.Reader.ReadAllAsync(ct))
        {
            lock (_lock)
            {
                CurrentModelId = id;
                _pending.Remove(id);
            }
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ScrapeService>().RunAsync(id, trigger, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Scrape of watched model {Id} crashed", id);
            }
            finally
            {
                CurrentModelId = null;
            }
        }
    }

    private async Task ScheduleLoopAsync(CancellationToken ct)
    {
        var dailyAt = TimeOnly.Parse(config["Scrape:DailyAt"] ?? "03:00");
        try
        {
            // Catch up if the container was down (e.g. a deploy) when the last run was due.
            if (await HoursSinceLastScheduledRunAsync(ct) is null or > 26)
                await EnqueueAllActiveAsync("schedule", ct);

            while (!ct.IsCancellationRequested)
            {
                var delay = UntilNext(dailyAt);
                logger.LogInformation("Next scheduled scrape in {Delay}", delay);
                await Task.Delay(delay, ct);
                await EnqueueAllActiveAsync("schedule", ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scrape schedule loop stopped");
        }
    }

    private async Task<double?> HoursSinceLastScheduledRunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VrooDbContext>();
        var last = await db.ScrapeRuns.Where(r => r.Trigger == "schedule")
            .MaxAsync(r => (DateTime?)r.StartedAt, ct);
        return last is null ? null : (DateTime.UtcNow - last.Value).TotalHours;
    }

    private static TimeSpan UntilNext(TimeOnly at)
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ScrapeService.Stockholm);
        var next = nowLocal.Date + at.ToTimeSpan();
        if (next <= nowLocal) next = next.AddDays(1);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(next, DateTimeKind.Unspecified), ScrapeService.Stockholm);
        return nextUtc - DateTime.UtcNow;
    }
}
