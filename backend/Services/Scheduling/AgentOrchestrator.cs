using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Applications;
using JYCareerAgent.Api.Services.CV;
using JYCareerAgent.Api.Services.Email;
using JYCareerAgent.Api.Services.JobSources;
using JYCareerAgent.Api.Services.Matching;
using JYCareerAgent.Api.Services.Storage;

namespace JYCareerAgent.Api.Services.Scheduling;

public class AgentOrchestrator
{
    private readonly IJsonStorageService _storageService;
    private readonly JobSourceFactory _sourceFactory;
    private readonly IJobNormalizationService _normalizationService;
    private readonly IJobMatchingEngine _matchingEngine;
    private readonly ICvManagerService _cvManagerService;
    private readonly IApplicationWorkflowService _workflowService;
    private readonly IEmailReportingService _emailService;
    private readonly ILogger<AgentOrchestrator> _logger;

    private static readonly SemaphoreSlim RunLock = new(1, 1);

    public AgentOrchestrator(
        IJsonStorageService storageService,
        JobSourceFactory sourceFactory,
        IJobNormalizationService normalizationService,
        IJobMatchingEngine matchingEngine,
        ICvManagerService cvManagerService,
        IApplicationWorkflowService workflowService,
        IEmailReportingService emailService,
        ILogger<AgentOrchestrator> logger)
    {
        _storageService = storageService;
        _sourceFactory = sourceFactory;
        _normalizationService = normalizationService;
        _matchingEngine = matchingEngine;
        _cvManagerService = cvManagerService;
        _workflowService = workflowService;
        _emailService = emailService;
        _logger = logger;
    }

    public bool IsRunning => RunLock.CurrentCount == 0;

    public async Task<bool> RunCycleAsync(string trigger = "Scheduler", CancellationToken cancellationToken = default)
    {
        // 1. In-process concurrency guard
        if (!await RunLock.WaitAsync(100, cancellationToken))
        {
            _logger.LogWarning("Execution cycle is already in progress in this instance. Skipping overlapping run trigger ({Trigger}).", trigger);
            return false;
        }

        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stageStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var startTimeUtc = DateTime.UtcNow;
        var karachiZone = JobSchedulerBackgroundService.GetKarachiTimeZone();
        var startTimeKarachi = TimeZoneInfo.ConvertTimeFromUtc(startTimeUtc, karachiZone);
        var correlationId = Guid.NewGuid().ToString("N");

        _logger.LogInformation("================================================================================");
        _logger.LogInformation(">>> JY CAREER AGENT EXECUTION CYCLE STARTED <<<");
        _logger.LogInformation("Trigger:               {Trigger}", trigger);
        _logger.LogInformation("Correlation ID:        {CorrelationId}", correlationId);
        _logger.LogInformation("Scheduled Start (PKT): {StartTimeKarachi:yyyy-MM-dd HH:mm:ss} (Asia/Karachi, UTC+5)", startTimeKarachi);
        _logger.LogInformation("Scheduled Start (UTC): {StartTimeUtc:yyyy-MM-dd HH:mm:ss} UTC", startTimeUtc);
        _logger.LogInformation("================================================================================");

        var agentState = await _storageService.LoadAsync<AgentState>("data/agent-state.json", cancellationToken) ?? new AgentState();

        // 2. Durable state concurrency guard
        if (string.Equals(agentState.Status, "Running", StringComparison.OrdinalIgnoreCase))
        {
            var runningDuration = agentState.LastRunStartTime.HasValue 
                ? DateTime.UtcNow - agentState.LastRunStartTime.Value 
                : TimeSpan.Zero;

            if (runningDuration < TimeSpan.FromMinutes(15))
            {
                _logger.LogWarning("An execution cycle is already active in durable state (started {Duration:F1}m ago, CorrelationId: {LastCorrelationId}). Skipping overlapping trigger ({Trigger}).",
                    runningDuration.TotalMinutes, agentState.LastCorrelationId, trigger);
                RunLock.Release();
                return false;
            }
            else
            {
                _logger.LogWarning("Previous execution cycle was marked 'Running' but exceeded 15 minutes. Treating as interrupted and reclaiming lock.");
            }
        }

        // 3. For scheduled triggers, prevent running twice on the same calendar day in PKT
        if (string.Equals(trigger, "Scheduler", StringComparison.OrdinalIgnoreCase) && 
            agentState.LastRunEndTime.HasValue && 
            string.Equals(agentState.LastRunStatus, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            var lastCompletedKarachi = TimeZoneInfo.ConvertTimeFromUtc(agentState.LastRunEndTime.Value, karachiZone);
            if (lastCompletedKarachi.Date == startTimeKarachi.Date)
            {
                _logger.LogInformation("Scheduled cycle for today ({Date:yyyy-MM-dd} PKT) has already completed successfully at {Time:HH:mm:ss} PKT. Skipping duplicate run.", 
                    startTimeKarachi.Date, lastCompletedKarachi);
                RunLock.Release();
                return true;
            }
        }

        agentState.Status = "Running";
        agentState.LastRunStartTime = startTimeUtc;
        agentState.LastCorrelationId = correlationId;
        agentState.LastError = null;
        agentState.CurrentJobIndex = 0;
        await _storageService.SaveAsync("data/agent-state.json", agentState, createBackup: false, cancellationToken);

        try
        {
            var settings = await _storageService.LoadAsync<AgentSettings>("data/settings.json", cancellationToken) ?? new AgentSettings();
            var profile = await _cvManagerService.GetProfileAsync(cancellationToken);

            // STAGE 1: Automatic pre-run backup
            stageStopwatch.Restart();
            _logger.LogInformation("[STAGE 1/7] Pre-run Backup: Creating backups of data files...");
            if (settings.Backup.BackupBeforeRun)
            {
                try
                {
                    await _storageService.CreateBackupAsync("data/job-history.json", cancellationToken);
                    await _storageService.CreateBackupAsync("data/applications.json", cancellationToken);
                    _logger.LogInformation("[STAGE 1/7 COMPLETED] Pre-run backup completed in {ElapsedMs} ms.", stageStopwatch.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[STAGE 1/7 WARNING] Pre-run backup failed non-fatally in {ElapsedMs} ms: {Message}", 
                        stageStopwatch.ElapsedMilliseconds, SanitizeError(ex.Message));
                }
            }
            else
            {
                _logger.LogInformation("[STAGE 1/7 SKIPPED] BackupBeforeRun is disabled in settings.");
            }

            // STAGE 2: Load existing job history
            stageStopwatch.Restart();
            _logger.LogInformation("[STAGE 2/7] Loading candidate profile and job history...");
            var existingHistory = await _storageService.LoadAsync<List<JobListing>>("data/job-history.json", cancellationToken) ?? new List<JobListing>();
            _logger.LogInformation("[STAGE 2/7 COMPLETED] Candidate '{FullName}' loaded. {Count} historical jobs in history ({ElapsedMs} ms).", 
                profile.FullName, existingHistory.Count, stageStopwatch.ElapsedMilliseconds);

            // STAGE 3: Query active job sources independently
            stageStopwatch.Restart();
            var sources = _sourceFactory.GetActiveSources(settings).ToList();
            _logger.LogInformation("[STAGE 3/7] Job Discovery: Querying {Count} active job sources independently...", sources.Count);
            var sourceSummaries = new List<SourceRunSummary>();
            var allDiscoveredJobs = new List<JobListing>();

            foreach (var src in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var srcStopwatch = System.Diagnostics.Stopwatch.StartNew();
                _logger.LogInformation("  -> Searching source '{SourceName}' ({AccessType})...", src.SourceName, src.AccessType);
                try
                {
                    var res = await src.SearchJobsAsync(settings, cancellationToken);
                    srcStopwatch.Stop();
                    var sanitizedError = SanitizeError(res.ErrorMessage);

                    sourceSummaries.Add(new SourceRunSummary
                    {
                        SourceName = src.SourceName,
                        Success = res.Success,
                        JobsFound = res.Jobs.Count,
                        ErrorMessage = sanitizedError
                    });

                    if (res.Success)
                    {
                        _logger.LogInformation("  [SUCCESS] Source '{SourceName}': Discovered {Count} jobs in {ElapsedMs} ms.", 
                            src.SourceName, res.Jobs.Count, srcStopwatch.ElapsedMilliseconds);
                        allDiscoveredJobs.AddRange(res.Jobs);
                    }
                    else
                    {
                        _logger.LogWarning("  [FAILED] Source '{SourceName}': Returned failure in {ElapsedMs} ms. Error: {Error}", 
                            src.SourceName, srcStopwatch.ElapsedMilliseconds, sanitizedError);
                    }
                }
                catch (Exception ex)
                {
                    srcStopwatch.Stop();
                    var sanitizedError = SanitizeError(ex.Message);
                    _logger.LogError(ex, "  [ERROR] Source '{SourceName}' threw exception in {ElapsedMs} ms: {Error}", 
                        src.SourceName, srcStopwatch.ElapsedMilliseconds, sanitizedError);
                    sourceSummaries.Add(new SourceRunSummary
                    {
                        SourceName = src.SourceName,
                        Success = false,
                        JobsFound = 0,
                        ErrorMessage = sanitizedError
                    });
                }
            }
            _logger.LogInformation("[STAGE 3/7 COMPLETED] Job Discovery finished in {ElapsedMs} ms. Total raw jobs discovered: {TotalJobs}.", 
                stageStopwatch.ElapsedMilliseconds, allDiscoveredJobs.Count);

            // STAGE 4: Normalization & Deduplication
            stageStopwatch.Restart();
            _logger.LogInformation("[STAGE 4/7] Normalization & Deduplication: Filtering out existing and duplicate jobs...");
            var uniqueNewJobs = new List<JobListing>();
            var duplicatesCount = 0;

            foreach (var rawJob in allDiscoveredJobs)
            {
                _normalizationService.NormalizeJob(rawJob);

                var isDup = _normalizationService.IsDuplicate(rawJob, existingHistory) || 
                            _normalizationService.IsDuplicate(rawJob, uniqueNewJobs);

                if (isDup)
                {
                    duplicatesCount++;
                }
                else
                {
                    uniqueNewJobs.Add(rawJob);
                }
            }
            _logger.LogInformation("[STAGE 4/7 COMPLETED] Normalization & Deduplication finished in {ElapsedMs} ms: {Unique} unique new jobs, {Duplicates} duplicates skipped.", 
                stageStopwatch.ElapsedMilliseconds, uniqueNewJobs.Count, duplicatesCount);

            // STAGE 5: Match Scoring & Evaluation
            stageStopwatch.Restart();
            _logger.LogInformation("[STAGE 5/7] Match Scoring: Evaluating {Count} unique new opportunities...", uniqueNewJobs.Count);
            foreach (var job in uniqueNewJobs)
            {
                job.MatchScore = _matchingEngine.EvaluateJob(job, profile, settings);
            }

            // Append to job history
            existingHistory.AddRange(uniqueNewJobs);
            await _storageService.SaveAsync("data/job-history.json", existingHistory, createBackup: false, cancellationToken);

            var highMatchCount = uniqueNewJobs.Count(j => j.MatchScore != null && j.MatchScore.TotalScore >= settings.Thresholds.HighPriority);
            
            // Respect eligibility filters: genuine match, skills/seniority, and location/remote alignment
            var eligibleJobs = uniqueNewJobs
                .Where(j => j.MatchScore != null && 
                            j.MatchScore.TotalScore >= settings.Thresholds.ApprovalQueue &&
                            (j.MatchScore.LocationScore > 30 || j.WorkArrangement == WorkArrangement.Remote) &&
                            j.MatchScore.SeniorityScore >= 50)
                .OrderByDescending(j => j.MatchScore!.TotalScore)
                .ToList();

            _logger.LogInformation("[STAGE 5/7 COMPLETED] Match Scoring finished in {ElapsedMs} ms. High Priority (80+): {HighMatchCount}, Approval Queue (65+): {EligibleCount}.", 
                stageStopwatch.ElapsedMilliseconds, highMatchCount, eligibleJobs.Count);

            // Determine effective operating mode (environment variable override takes precedence if set)
            var effectiveOperatingMode = settings.OperatingMode;
            var opModeEnv = Environment.GetEnvironmentVariable("JY_OPERATING_MODE");
            if (!string.IsNullOrWhiteSpace(opModeEnv) && Enum.TryParse<OperatingMode>(opModeEnv, true, out var parsedMode))
            {
                effectiveOperatingMode = parsedMode;
            }
            _logger.LogInformation("Agent Operating Mode: {Mode} (Settings: {SettingsMode}, EnvOverride: {EnvOverride})",
                effectiveOperatingMode, settings.OperatingMode, opModeEnv ?? "None");

            // STAGE 6: Application Preparation & Submission (Resumable Workflow)
            stageStopwatch.Restart();
            _logger.LogInformation("[STAGE 6/7] Application Preparation & Submission: Processing {Count} eligible opportunities (Threshold >= {Threshold})...", 
                eligibleJobs.Count, settings.Thresholds.ApprovalQueue);

            var applicationsProcessed = new List<JobApplication>();
            int appIndex = 0;

            foreach (var job in eligibleJobs)
            {
                appIndex++;
                cancellationToken.ThrowIfCancellationRequested();
                var appStopwatch = System.Diagnostics.Stopwatch.StartNew();
                _logger.LogInformation("  -> [{Index}/{Total}] Preparing application for '{Employer}' - '{Title}' (Score: {Score:F0}%)...", 
                    appIndex, eligibleJobs.Count, job.Employer, job.Title, job.MatchScore?.TotalScore ?? 0);

                try
                {
                    var app = await _workflowService.PrepareApplicationAsync(job, profile, settings, cancellationToken);

                    // In Automatic mode, attempt automated submission (workflow handles platform routing, screening verification, and daily limit)
                    if (effectiveOperatingMode == OperatingMode.Automatic)
                    {
                        app = await _workflowService.SubmitApplicationAsync(app.Id, userExplicitApproval: false, cancellationToken);
                    }

                    applicationsProcessed.Add(app);
                    appStopwatch.Stop();
                    _logger.LogInformation("  [DONE] Application {AppId} status: {Status} (Processed in {ElapsedMs} ms)", 
                        app.Id, app.Status, appStopwatch.ElapsedMilliseconds);

                    // Update resumable durable state checkpoint
                    agentState.CurrentJobIndex = appIndex;
                    agentState.ApplicationsSubmittedToday = applicationsProcessed.Count(a => a.Status == ApplicationStatus.Submitted);
                    agentState.ApplicationsPendingToday = applicationsProcessed.Count(a => a.Status == ApplicationStatus.PendingApproval || a.Status == ApplicationStatus.ReadyToSubmit);
                    agentState.ApplicationsManualActionToday = applicationsProcessed.Count(a => a.Status == ApplicationStatus.ManualActionRequired);
                    agentState.ApplicationsSkippedToday = applicationsProcessed.Count(a => a.Status == ApplicationStatus.Skipped);
                    agentState.ApplicationsFailedToday = applicationsProcessed.Count(a => a.Status == ApplicationStatus.Failed);
                    await _storageService.SaveAsync("data/agent-state.json", agentState, createBackup: false, cancellationToken);
                }
                catch (Exception ex)
                {
                    appStopwatch.Stop();
                    _logger.LogError(ex, "  [ERROR] Failed preparing application for job {JobId} ({Employer}) in {ElapsedMs} ms: {Error}", 
                        job.Id, job.Employer, appStopwatch.ElapsedMilliseconds, SanitizeError(ex.Message));
                }
            }
            _logger.LogInformation("[STAGE 6/7 COMPLETED] Application Processing finished in {ElapsedMs} ms. Processed: {Count} applications.", 
                stageStopwatch.ElapsedMilliseconds, applicationsProcessed.Count);

            var endTimeUtc = DateTime.UtcNow;

            // STAGE 7: Generate and Send Mandatory Daily Email Summary
            stageStopwatch.Restart();
            _logger.LogInformation("[STAGE 7/7] Daily Email Reporting: Generating and dispatching execution report...");
            var report = await _emailService.GenerateDailyReportRecordAsync(
                startTimeUtc, 
                endTimeUtc, 
                sourceSummaries, 
                allDiscoveredJobs, 
                uniqueNewJobs, 
                applicationsProcessed, 
                duplicatesCount, 
                cancellationToken);

            var emailSuccess = await _emailService.SendDailyReportAsync(report, cancellationToken);
            stageStopwatch.Stop();
            _logger.LogInformation("[STAGE 7/7 COMPLETED] Daily Report sent to '{Recipient}' in {ElapsedMs} ms (DeliverySuccess: {Success}, Message: {Message}).", 
                report.RecipientEmail, stageStopwatch.ElapsedMilliseconds, emailSuccess, SanitizeError(report.DeliveryStatusMessage));

            // STAGE 8: Finalize Agent State
            totalStopwatch.Stop();
            agentState.Status = "Idle";
            agentState.LastRunEndTime = endTimeUtc;
            agentState.LastRunStatus = "Completed";
            agentState.TotalJobsFoundToday = allDiscoveredJobs.Count;
            agentState.UniqueJobsFoundToday = uniqueNewJobs.Count;
            agentState.MatchedJobsToday = eligibleJobs.Count;
            agentState.ApplicationsSubmittedToday = applicationsProcessed.Count(a => a.Status == ApplicationStatus.Submitted);
            agentState.ApplicationsPendingToday = applicationsProcessed.Count(a => a.Status == ApplicationStatus.PendingApproval || a.Status == ApplicationStatus.ReadyToSubmit);
            agentState.ApplicationsManualActionToday = applicationsProcessed.Count(a => a.Status == ApplicationStatus.ManualActionRequired);
            agentState.ApplicationsSkippedToday = applicationsProcessed.Count(a => a.Status == ApplicationStatus.Skipped);
            agentState.ApplicationsFailedToday = applicationsProcessed.Count(a => a.Status == ApplicationStatus.Failed);
            agentState.LastError = null;
            await _storageService.SaveAsync("data/agent-state.json", agentState, createBackup: false, cancellationToken);

            _logger.LogInformation("================================================================================");
            _logger.LogInformation(">>> JY CAREER AGENT RUN CYCLE COMPLETED SUCCESSFULLY in {TotalSeconds:F1}s <<<", totalStopwatch.Elapsed.TotalSeconds);
            _logger.LogInformation("Discovered:  {Discovered} raw jobs", allDiscoveredJobs.Count);
            _logger.LogInformation("Unique New:  {Unique} jobs", uniqueNewJobs.Count);
            _logger.LogInformation("Duplicates:  {Duplicates} skipped", duplicatesCount);
            _logger.LogInformation("Eligible:    {Eligible} opportunities (Score >= {Threshold})", eligibleJobs.Count, settings.Thresholds.ApprovalQueue);
            _logger.LogInformation("Submitted:   {Submitted} applications", agentState.ApplicationsSubmittedToday);
            _logger.LogInformation("Pending:     {Pending} applications awaiting approval", agentState.ApplicationsPendingToday);
            _logger.LogInformation("Email Sent:  {EmailSent} ({StatusMessage})", emailSuccess, SanitizeError(report.DeliveryStatusMessage));
            _logger.LogInformation("================================================================================");

            return true;
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            var sanitizedError = SanitizeError(ex.Message);
            _logger.LogError(ex, ">>> JY CAREER AGENT CYCLE ENCOUNTERED FATAL ERROR after {TotalSeconds:F1}s: {Error} <<<", 
                totalStopwatch.Elapsed.TotalSeconds, sanitizedError);

            agentState.Status = "Failed";
            agentState.LastRunEndTime = DateTime.UtcNow;
            agentState.LastRunStatus = "Failed";
            agentState.LastError = sanitizedError;
            await _storageService.SaveAsync("data/agent-state.json", agentState, createBackup: false, cancellationToken);
            return false;
        }
        finally
        {
            RunLock.Release();
        }
    }

    public static string SanitizeError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "None";

        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            message, 
            @"(?i)(password|pwd|secret|apikey|api_key|token|bearer)\s*[:=]\s*([^\s,;]+)", 
            "$1=***REDACTED***");

        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized, 
            @"bearer\s+[a-zA-Z0-9_\-\.]+", 
            "Bearer ***REDACTED***", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var envSecrets = new[]
        {
            Environment.GetEnvironmentVariable("JY_EMAIL__SMTP_PASSWORD"),
            Environment.GetEnvironmentVariable("JY_AI__API_KEY"),
            Environment.GetEnvironmentVariable("JY_SCHEDULER_SECRET")
        };

        foreach (var secret in envSecrets)
        {
            if (!string.IsNullOrWhiteSpace(secret) && secret.Length > 3)
            {
                sanitized = sanitized.Replace(secret, "***REDACTED***");
            }
        }

        return sanitized;
    }
}
