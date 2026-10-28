using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Scheduling;
using JYCareerAgent.Api.Services.Storage;
using Microsoft.AspNetCore.Mvc;

namespace JYCareerAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SchedulerController : ControllerBase
{
    private readonly AgentOrchestrator _orchestrator;
    private readonly IJsonStorageService _storageService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SchedulerController> _logger;
    private readonly IHostApplicationLifetime _appLifetime;

    public SchedulerController(
        AgentOrchestrator orchestrator,
        IJsonStorageService storageService,
        IConfiguration configuration,
        ILogger<SchedulerController> logger,
        IHostApplicationLifetime appLifetime)
    {
        _orchestrator = orchestrator;
        _storageService = storageService;
        _configuration = configuration;
        _logger = logger;
        _appLifetime = appLifetime;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var state = await _storageService.LoadAsync<AgentState>("data/agent-state.json", cancellationToken) ?? new AgentState();
        var settings = await _storageService.LoadAsync<AgentSettings>("data/settings.json", cancellationToken) ?? new AgentSettings();

        var karachiZone = JobSchedulerBackgroundService.GetKarachiTimeZone();
        var nowUtc = DateTime.UtcNow;
        var nowKarachi = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, karachiZone);

        DateTime? nextRunKarachi = state.NextScheduledRun.HasValue
            ? TimeZoneInfo.ConvertTimeFromUtc(state.NextScheduledRun.Value, karachiZone)
            : null;

        return Ok(new
        {
            IsRunning = _orchestrator.IsRunning,
            state.Status,
            state.LastRunStatus,
            state.LastRunStartTime,
            state.LastRunEndTime,
            state.NextScheduledRun,
            NextScheduledRunKarachi = nextRunKarachi,
            CurrentTimeUtc = nowUtc,
            CurrentTimeKarachi = nowKarachi,
            Timezone = "Asia/Karachi (UTC+5)",
            ScheduleEnabled = settings.Schedule.Enabled,
            ScheduledTimeOfDay = settings.Schedule.ScheduledTime,
            state.LastCorrelationId,
            state.CurrentJobIndex,
            state.TotalJobsFoundToday,
            state.UniqueJobsFoundToday,
            state.MatchedJobsToday,
            state.ApplicationsSubmittedToday,
            state.ApplicationsPendingToday,
            state.ApplicationsFailedToday
        });
    }

    [HttpPost("trigger")]
    public IActionResult TriggerScheduledRun(
        [FromHeader(Name = "X-Scheduler-Key")] string? headerKey,
        [FromQuery] string? key)
    {
        // 1. Verify optional secret token if configured in environment
        var configuredSecret = Environment.GetEnvironmentVariable("JY_SCHEDULER_SECRET") ?? 
                               _configuration["JY_SCHEDULER_SECRET"];

        if (!string.IsNullOrWhiteSpace(configuredSecret))
        {
            var providedKey = headerKey ?? key;
            var authHeader = Request.Headers.Authorization.ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                providedKey = authHeader["Bearer ".Length..].Trim();
            }

            if (!string.Equals(configuredSecret, providedKey, StringComparison.Ordinal))
            {
                _logger.LogWarning("Unauthorized scheduler trigger attempt from {IpAddress}", HttpContext.Connection.RemoteIpAddress);
                return Unauthorized(new { message = "Invalid scheduler authorization token." });
            }
        }

        // 2. Concurrency check
        if (_orchestrator.IsRunning)
        {
            return Conflict(new
            {
                status = "AlreadyRunning",
                message = "An execution cycle is already running. Overlapping run avoided."
            });
        }

        // 3. Launch independently of the HTTP request lifecycle
        var stoppingToken = _appLifetime.ApplicationStopping;
        _ = Task.Run(async () =>
        {
            try
            {
                await _orchestrator.RunCycleAsync("ScheduledWebhook", stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background execution cycle triggered via ScheduledWebhook encountered fatal error: {Message}", 
                    AgentOrchestrator.SanitizeError(ex.Message));
            }
        }, CancellationToken.None);

        var karachiZone = JobSchedulerBackgroundService.GetKarachiTimeZone();
        var nowKarachi = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, karachiZone);

        return Accepted(new
        {
            status = "Accepted",
            message = "JY Career Agent execution cycle launched independently in background.",
            trigger = "ScheduledWebhook",
            triggerTimeKarachi = nowKarachi.ToString("yyyy-MM-dd HH:mm:ss 'PKT'"),
            scheduledTimeTarget = "09:00:00 Asia/Karachi",
            statusEndpoint = "/api/scheduler/status",
            dashboardEndpoint = "/api/dashboard/stats"
        });
    }
}
