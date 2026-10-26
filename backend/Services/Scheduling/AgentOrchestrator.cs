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
        if (!await RunLock.WaitAsync(100, cancellationToken))
        {
            _logger.LogWarning("Execution cycle is already in progress. Skipping overlapping run trigger ({Trigger}).", trigger);
            return false;
        }

        var startTimeUtc = DateTime.UtcNow;
        var correlationId = Guid.NewGuid().ToString("N");
        _logger.LogInformation(">>> Starting JY Career Agent Run Cycle. Trigger: {Trigger}, CorrelationId: {CorrelationId} <<<", trigger, correlationId);

        var agentState = await _storageService.LoadAsync<AgentState>("data/agent-state.json", cancellationToken) ?? new AgentState();
        agentState.Status = "Running";
        agentState.LastRunStartTime = startTimeUtc;
        agentState.LastCorrelationId = correlationId;
        agentState.LastError = null;
        await _storageService.SaveAsync("data/agent-state.json", agentState, createBackup: false, cancellationToken);

        try
        {
            var settings = await _storageService.LoadAsync<AgentSettings>("data/settings.json", cancellationToken) ?? new AgentSettings();
            var profile = await _cvManagerService.GetProfileAsync(cancellationToken);

            // 1. Automatic pre-run backup
            if (settings.Backup.BackupBeforeRun)
            {
                try
                {
                    await _storageService.CreateBackupAsync("data/job-history.json", cancellationToken);
                    await _storageService.CreateBackupAsync("data/applications.json", cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Pre-run backup failed non-fatally. Continuing run.");
                }
            }

            // 2. Load existing job history
            var existingHistory = await _storageService.LoadAsync<List<JobListing>>("data/job-history.json", cancellationToken) ?? new List<JobListing>();

            // 3. Query active job sources independently
            var sources = _sourceFactory.GetActiveSources(settings).ToList();
            var sourceSummaries = new List<SourceRunSummary>();
            var allDiscoveredJobs = new List<JobListing>();

            foreach (var src in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var res = await src.SearchJobsAsync(settings, cancellationToken);
                    sourceSummaries.Add(new SourceRunSummary
                    {
                        SourceName = src.SourceName,
                        Success = res.Success,
                        JobsFound = res.Jobs.Count,
                        ErrorMessage = res.ErrorMessage
                    });

                    if (res.Success)
                    {
                        allDiscoveredJobs.AddRange(res.Jobs);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Source {SourceName} failed during execution cycle", src.SourceName);
                    sourceSummaries.Add(new SourceRunSummary
                    {
                        SourceName = src.SourceName,
                        Success = false,
                        JobsFound = 0,
                        ErrorMessage = ex.Message
                    });
                }
            }

            // 4. Normalization & Deduplication
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

            _logger.LogInformation("Job Discovery Summary: {Total} raw, {Unique} unique new, {Duplicates} duplicates skipped.", 
                allDiscoveredJobs.Count, uniqueNewJobs.Count, duplicatesCount);

            // 5. Match Scoring & Evaluation
            foreach (var job in uniqueNewJobs)
            {
                job.MatchScore = _matchingEngine.EvaluateJob(job, profile, settings);
            }

            // Append to job history
            existingHistory.AddRange(uniqueNewJobs);
            await _storageService.SaveAsync("data/job-history.json", existingHistory, createBackup: false, cancellationToken);

            // 6. Application Preparation for Eligible Opportunities
            var eligibleJobs = uniqueNewJobs
                .Where(j => j.MatchScore != null && j.MatchScore.TotalScore >= settings.Thresholds.ApprovalQueue)
                .OrderByDescending(j => j.MatchScore!.TotalScore)
                .ToList();

            var applicationsProcessed = new List<JobApplication>();
            foreach (var job in eligibleJobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var app = await _workflowService.PrepareApplicationAsync(job, profile, settings, cancellationToken);

                    // If operating mode is Automatic AND role permits automated workflow, attempt submission
                    if (settings.OperatingMode == OperatingMode.Automatic && 
                        !job.SourcePlatform.Equals("LinkedIn", StringComparison.OrdinalIgnoreCase) &&
                        !job.SourcePlatform.Equals("Indeed", StringComparison.OrdinalIgnoreCase))
                    {
                        app = await _workflowService.SubmitApplicationAsync(app.Id, userExplicitApproval: false, cancellationToken);
                    }

                    applicationsProcessed.Add(app);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed preparing application for job {JobId} ({Employer})", job.Id, job.Employer);
                }
            }

            var endTimeUtc = DateTime.UtcNow;

            // 7. Update Agent State
            agentState.Status = "Idle";
            agentState.LastRunEndTime = endTimeUtc;
            agentState.LastRunStatus = "Completed";
            agentState.TotalJobsFoundToday = allDiscoveredJobs.Count;
            agentState.UniqueJobsFoundToday = uniqueNewJobs.Count;
            agentState.MatchedJobsToday = eligibleJobs.Count;
            agentState.ApplicationsSubmittedToday = applicationsProcessed.Count(a => a.Status == ApplicationStatus.Submitted);
            agentState.ApplicationsPendingToday = applicationsProcessed.Count(a => a.Status == ApplicationStatus.PendingApproval || a.Status == ApplicationStatus.ReadyToSubmit);
            agentState.ApplicationsFailedToday = applicationsProcessed.Count(a => a.Status == ApplicationStatus.Failed);
            await _storageService.SaveAsync("data/agent-state.json", agentState, createBackup: false, cancellationToken);

            // 8. Generate and Send Mandatory Daily Email Summary
            var report = await _emailService.GenerateDailyReportRecordAsync(
                startTimeUtc, 
                endTimeUtc, 
                sourceSummaries, 
                allDiscoveredJobs, 
                uniqueNewJobs, 
                applicationsProcessed, 
                duplicatesCount, 
                cancellationToken);

            await _emailService.SendDailyReportAsync(report, cancellationToken);

            _logger.LogInformation(">>> JY Career Agent Run Cycle Completed Successfully <<<");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JY Career Agent cycle encountered fatal error");
            agentState.Status = "Failed";
            agentState.LastRunEndTime = DateTime.UtcNow;
            agentState.LastRunStatus = "Failed";
            agentState.LastError = ex.Message;
            await _storageService.SaveAsync("data/agent-state.json", agentState, createBackup: false, cancellationToken);
            return false;
        }
        finally
        {
            RunLock.Release();
        }
    }
}
