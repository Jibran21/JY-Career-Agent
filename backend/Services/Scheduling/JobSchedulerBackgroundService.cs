using System.Text.RegularExpressions;
using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Storage;

namespace JYCareerAgent.Api.Services.Scheduling;

public class JobSchedulerBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JobSchedulerBackgroundService> _logger;
    private DateTime? _lastLoggedNextRunUtc;

    public JobSchedulerBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<JobSchedulerBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JY Career Agent Scheduler BackgroundService started. Target: Daily at 09:00:00 Asia/Karachi (PKT, UTC+5).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var storage = scope.ServiceProvider.GetRequiredService<IJsonStorageService>();
                var orchestrator = scope.ServiceProvider.GetRequiredService<AgentOrchestrator>();

                var settings = await storage.LoadAsync<AgentSettings>("data/settings.json", stoppingToken) ?? new AgentSettings();

                if (!settings.Schedule.Enabled)
                {
                    _logger.LogInformation("Daily scheduler is disabled in settings. Pausing checks for 30 minutes.");
                    await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                    continue;
                }

                // Parse scheduled time of day (default: 09:00:00 AM)
                if (!TimeSpan.TryParse(settings.Schedule.ScheduledTime, out var scheduledTimeOfDay))
                {
                    scheduledTimeOfDay = new TimeSpan(9, 0, 0);
                }

                var karachiZone = GetKarachiTimeZone();
                var nowUtc = DateTime.UtcNow;
                var nowKarachi = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, karachiZone);

                // Load existing state to inspect last run time and status
                var state = await storage.LoadAsync<AgentState>("data/agent-state.json", stoppingToken) ?? new AgentState();

                // Compute next scheduled run or detect missed catch-up
                var (nextRunKarachi, isMissedCatchUp) = CalculateNextRun(
                    nowKarachi, 
                    scheduledTimeOfDay, 
                    state.LastRunStartTime, 
                    state.LastRunStatus, 
                    karachiZone);

                var nextRunUtc = TimeZoneInfo.ConvertTimeToUtc(nextRunKarachi, karachiZone);
                var delay = nextRunUtc - nowUtc;

                // Update durable state only if changed
                if (state.NextScheduledRun != nextRunUtc)
                {
                    state.NextScheduledRun = nextRunUtc;
                    await storage.SaveAsync("data/agent-state.json", state, createBackup: false, stoppingToken);
                }

                // Log next scheduled run (throttled to avoid log flooding)
                if (_lastLoggedNextRunUtc != nextRunUtc)
                {
                    _lastLoggedNextRunUtc = nextRunUtc;
                    if (isMissedCatchUp)
                    {
                        _logger.LogWarning(">>> MISSED SCHEDULE DETECTED <<< Current PKT time is {NowKarachi:yyyy-MM-dd HH:mm:ss}, but 09:00 AM PKT run for today was not completed. Triggering immediate catch-up cycle...", nowKarachi);
                    }
                    else
                    {
                        _logger.LogInformation("Next scheduled run is at {NextRunKarachi:yyyy-MM-dd HH:mm:ss} PKT / {NextRunUtc:yyyy-MM-dd HH:mm:ss} UTC (Delay: {Hours:F1}h {Minutes:F0}m)", 
                            nextRunKarachi, nextRunUtc, delay.TotalHours, delay.Minutes);
                    }
                }

                if (isMissedCatchUp || delay <= TimeSpan.Zero)
                {
                    _logger.LogInformation("Launching scheduled daily cycle (Trigger: Scheduler)...");
                    await orchestrator.RunCycleAsync("Scheduler", stoppingToken);

                    // Re-read state after run to reset next run to tomorrow
                    var postRunState = await storage.LoadAsync<AgentState>("data/agent-state.json", stoppingToken) ?? new AgentState();
                    var postRunNowKarachi = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, karachiZone);
                    var (postRunNextKarachi, _) = CalculateNextRun(
                        postRunNowKarachi, 
                        scheduledTimeOfDay, 
                        postRunState.LastRunStartTime, 
                        postRunState.LastRunStatus, 
                        karachiZone);

                    var postRunNextUtc = TimeZoneInfo.ConvertTimeToUtc(postRunNextKarachi, karachiZone);
                    postRunState.NextScheduledRun = postRunNextUtc;
                    await storage.SaveAsync("data/agent-state.json", postRunState, createBackup: false, stoppingToken);

                    _lastLoggedNextRunUtc = postRunNextUtc;
                    _logger.LogInformation("Scheduled cycle finished. Next run updated to {NextRunKarachi:yyyy-MM-dd HH:mm:ss} PKT.", postRunNextKarachi);

                    // Pause briefly before re-entering wait loop
                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                    continue;
                }

                // Wait responsive delay chunks (e.g. up to 30 seconds)
                var delayMs = (int)Math.Min(delay.TotalMilliseconds, 30000);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in JobSchedulerBackgroundService. Retrying in 1 minute.");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        _logger.LogInformation("JobSchedulerBackgroundService stopped.");
    }

    public static (DateTime nextRunKarachi, bool isMissedCatchUp) CalculateNextRun(
        DateTime nowKarachi, 
        TimeSpan scheduledTimeOfDay, 
        DateTime? lastRunUtc, 
        string? lastRunStatus,
        TimeZoneInfo karachiZone)
    {
        var todayScheduledKarachi = nowKarachi.Date.Add(scheduledTimeOfDay);

        // Check if today's run has already completed in Karachi date
        bool alreadyRanToday = false;
        if (lastRunUtc.HasValue && string.Equals(lastRunStatus, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            var lastRunKarachi = TimeZoneInfo.ConvertTimeFromUtc(lastRunUtc.Value, karachiZone);
            if (lastRunKarachi.Date == nowKarachi.Date)
            {
                alreadyRanToday = true;
            }
        }

        if (alreadyRanToday)
        {
            return (todayScheduledKarachi.AddDays(1), false);
        }

        if (nowKarachi >= todayScheduledKarachi)
        {
            // Scheduled time has arrived or passed today without a completed run
            return (nowKarachi, true);
        }

        return (todayScheduledKarachi, false);
    }

    public static TimeZoneInfo GetKarachiTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi");
        }
        catch
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time");
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone("PKT", TimeSpan.FromHours(5), "Pakistan Standard Time", "PKT");
            }
        }
    }
}
