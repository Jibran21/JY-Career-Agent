using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Storage;

namespace JYCareerAgent.Api.Services.Scheduling;

public class JobSchedulerBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JobSchedulerBackgroundService> _logger;

    public JobSchedulerBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<JobSchedulerBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JY Career Agent Scheduler BackgroundService started.");

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
                    _logger.LogInformation("Daily scheduler is disabled in settings. Pausing check for 30 minutes.");
                    await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                    continue;
                }

                // Calculate next run time in Asia/Karachi
                var karachiZone = GetKarachiTimeZone();
                var nowUtc = DateTime.UtcNow;
                var nowKarachi = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, karachiZone);

                if (!TimeSpan.TryParse(settings.Schedule.ScheduledTime, out var scheduledTimeOfDay))
                {
                    scheduledTimeOfDay = new TimeSpan(9, 0, 0); // Default 9:00 AM
                }

                var todayScheduledKarachi = nowKarachi.Date.Add(scheduledTimeOfDay);
                DateTime nextRunKarachi;
                if (nowKarachi < todayScheduledKarachi)
                {
                    nextRunKarachi = todayScheduledKarachi;
                }
                else
                {
                    nextRunKarachi = todayScheduledKarachi.AddDays(1);
                }

                var nextRunUtc = TimeZoneInfo.ConvertTimeToUtc(nextRunKarachi, karachiZone);
                var delay = nextRunUtc - nowUtc;

                // Update agent state with next run time
                var state = await storage.LoadAsync<AgentState>("data/agent-state.json", stoppingToken) ?? new AgentState();
                state.NextScheduledRun = nextRunUtc;
                await storage.SaveAsync("data/agent-state.json", state, createBackup: false, stoppingToken);

                _logger.LogInformation("Next scheduled run is at {NextRunKarachi:yyyy-MM-dd HH:mm:ss} PKT (Delay: {Hours:F1}h {Minutes:F0}m)", 
                    nextRunKarachi, delay.TotalHours, delay.Minutes);

                if (delay > TimeSpan.Zero)
                {
                    // Delay until scheduled time with periodic check for stop token or manual triggers
                    var delayMs = (int)Math.Min(delay.TotalMilliseconds, 60000); // Check at least once a minute
                    await Task.Delay(delayMs, stoppingToken);

                    // Re-check if it's time
                    if (DateTime.UtcNow >= nextRunUtc)
                    {
                        _logger.LogInformation("Scheduled time reached ({NextRunKarachi}). Launching daily cycle...", nextRunKarachi);
                        await orchestrator.RunCycleAsync("Scheduler", stoppingToken);
                    }
                }
                else
                {
                    await orchestrator.RunCycleAsync("Scheduler", stoppingToken);
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

    private static TimeZoneInfo GetKarachiTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi");
        }
        catch
        {
            try
            {
                // Windows timezone ID fallback
                return TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time");
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone("PKT", TimeSpan.FromHours(5), "Pakistan Standard Time", "PKT");
            }
        }
    }
}
