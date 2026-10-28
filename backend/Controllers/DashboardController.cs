using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Applications;
using JYCareerAgent.Api.Services.Email;
using JYCareerAgent.Api.Services.JobSources;
using JYCareerAgent.Api.Services.Scheduling;
using JYCareerAgent.Api.Services.Storage;
using Microsoft.AspNetCore.Mvc;

namespace JYCareerAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IJsonStorageService _storageService;
    private readonly AgentOrchestrator _orchestrator;
    private readonly IApplicationWorkflowService _workflowService;
    private readonly JobSourceFactory _sourceFactory;
    private readonly IEmailReportingService _emailService;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        IJsonStorageService storageService,
        AgentOrchestrator orchestrator,
        IApplicationWorkflowService workflowService,
        JobSourceFactory sourceFactory,
        IEmailReportingService emailService,
        IHostApplicationLifetime appLifetime,
        ILogger<DashboardController> logger)
    {
        _storageService = storageService;
        _orchestrator = orchestrator;
        _workflowService = workflowService;
        _sourceFactory = sourceFactory;
        _emailService = emailService;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetDashboardStats(CancellationToken cancellationToken)
    {
        var state = await _storageService.LoadAsync<AgentState>("data/agent-state.json", cancellationToken) ?? new AgentState();
        var settings = await _storageService.LoadAsync<AgentSettings>("data/settings.json", cancellationToken) ?? new AgentSettings();
        var jobs = await _storageService.LoadAsync<List<JobListing>>("data/job-history.json", cancellationToken) ?? new List<JobListing>();
        var applications = await _workflowService.GetApplicationsAsync(cancellationToken);
        var reports = await _emailService.GetReportHistoryAsync(cancellationToken);
        var latestReport = reports.FirstOrDefault();

        var todayConfirmedCount = await _workflowService.GetTodayConfirmedApplicationCountAsync(cancellationToken);

        var stats = new
        {
            state.Status,
            IsRunning = _orchestrator.IsRunning,
            state.LastRunStartTime,
            state.LastRunEndTime,
            state.LastRunStatus,
            state.NextScheduledRun,
            TotalJobsInHistory = jobs.Count,
            TotalJobsFoundToday = state.TotalJobsFoundToday,
            HighMatchJobsCount = jobs.Count(j => j.MatchScore?.TotalScore >= settings.Thresholds.HighPriority),
            ApplicationsSubmittedToday = todayConfirmedCount,
            MaxDailyApplications = settings.MaxDailyApplications,
            ApplicationsPendingApproval = applications.Count(a => a.Status == ApplicationStatus.PendingApproval || a.Status == ApplicationStatus.ReadyToSubmit),
            ApplicationsFailedOrUnconfirmed = applications.Count(a => a.Status == ApplicationStatus.Failed || a.Status == ApplicationStatus.SubmissionUnconfirmed),
            TotalApplications = applications.Count,
            OperatingMode = settings.OperatingMode.ToString(),
            LatestEmailReport = latestReport != null ? new
            {
                latestReport.ReportDate,
                latestReport.DeliverySuccess,
                latestReport.DeliveryStatusMessage,
                latestReport.SentAtUtc
            } : null,
            ActiveSources = _sourceFactory.GetActiveSources(settings).Select(s => new { s.SourceName, s.AccessType })
        };

        return Ok(stats);
    }

    [HttpPost("run-now")]
    public IActionResult TriggerManualRun()
    {
        if (_orchestrator.IsRunning)
        {
            return Conflict(new { message = "An execution cycle is already running." });
        }

        // Run asynchronously in detached background task using app lifetime stopping token
        var stoppingToken = _appLifetime.ApplicationStopping;
        _ = Task.Run(async () =>
        {
            try
            {
                await _orchestrator.RunCycleAsync("DashboardRunNow", stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background execution cycle triggered via Dashboard encountered fatal error: {Message}", 
                    AgentOrchestrator.SanitizeError(ex.Message));
            }
        }, CancellationToken.None);

        return Accepted(new { message = "JY Career Agent execution cycle launched successfully in background." });
    }

    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications(CancellationToken cancellationToken)
    {
        var notifications = await _storageService.LoadAsync<List<SystemNotification>>("data/notifications.json", cancellationToken) 
            ?? new List<SystemNotification>();
        return Ok(notifications);
    }
}
