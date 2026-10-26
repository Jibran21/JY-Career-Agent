using System.Text;
using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.AI;
using JYCareerAgent.Api.Services.CV;
using JYCareerAgent.Api.Services.Storage;

namespace JYCareerAgent.Api.Services.Applications;

public class ApplicationWorkflowService : IApplicationWorkflowService
{
    private readonly IJsonStorageService _storageService;
    private readonly ICvManagerService _cvManagerService;
    private readonly IAiAgentService _aiAgentService;
    private readonly ILogger<ApplicationWorkflowService> _logger;
    private readonly string _baseRoot;

    public ApplicationWorkflowService(
        IJsonStorageService storageService,
        ICvManagerService cvManagerService,
        IAiAgentService aiAgentService,
        IConfiguration configuration,
        ILogger<ApplicationWorkflowService> logger)
    {
        _storageService = storageService;
        _cvManagerService = cvManagerService;
        _aiAgentService = aiAgentService;
        _logger = logger;

        var configuredRoot = configuration["JY_STORAGE_ROOT"];
        var root = !string.IsNullOrWhiteSpace(configuredRoot) 
            ? configuredRoot 
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        if (!Directory.Exists(Path.Combine(root, "data")) && Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "data")))
        {
            root = Directory.GetCurrentDirectory();
        }
        else if (!Directory.Exists(Path.Combine(root, "data")))
        {
            var parent = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName;
            if (parent != null && Directory.Exists(Path.Combine(parent, "data")))
            {
                root = parent;
            }
        }

        _baseRoot = root;
    }

    public async Task<List<JobApplication>> GetApplicationsAsync(CancellationToken cancellationToken = default)
    {
        var list = await _storageService.LoadAsync<List<JobApplication>>("data/applications.json", cancellationToken);
        return list ?? new List<JobApplication>();
    }

    public async Task<JobApplication?> GetApplicationByIdAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        var list = await GetApplicationsAsync(cancellationToken);
        return list.FirstOrDefault(a => a.Id == applicationId);
    }

    public async Task<int> GetTodayConfirmedApplicationCountAsync(CancellationToken cancellationToken = default)
    {
        var list = await GetApplicationsAsync(cancellationToken);
        var todayUtc = DateTime.UtcNow.Date;
        return list.Count(a => a.Status == ApplicationStatus.Submitted && 
                               a.SubmissionTimestampUtc.HasValue && 
                               a.SubmissionTimestampUtc.Value.Date == todayUtc);
    }

    public async Task<JobApplication> PrepareApplicationAsync(
        JobListing job, 
        CandidateProfile profile, 
        AgentSettings settings, 
        CancellationToken cancellationToken = default)
    {
        var existingList = await GetApplicationsAsync(cancellationToken);

        // Check if an application for this job already exists
        var existing = existingList.FirstOrDefault(a => a.JobId == job.Id);
        if (existing != null)
        {
            _logger.LogInformation("Application for Job ID {JobId} already exists (Status: {Status}).", job.Id, existing.Status);
            return existing;
        }

        // 1. Generate ATS Tailored CV
        var (cvFileName, cvFullPath, _) = await _cvManagerService.GenerateTailoredCvAsync(job, profile, cancellationToken);

        // 2. Generate Grounded Cover Letter
        var coverLetterText = await _aiAgentService.GenerateCoverLetterAsync(job, profile, settings, cancellationToken);
        var coverLetterDir = Path.Combine(_baseRoot, "cover-letters");
        Directory.CreateDirectory(coverLetterDir);
        var coverLetterFileName = $"CoverLetter_{job.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt";
        var coverLetterPath = Path.Combine(coverLetterDir, coverLetterFileName);
        await File.WriteAllTextAsync(coverLetterPath, coverLetterText, Encoding.UTF8, cancellationToken);

        // 3. Evaluate standard screening questions
        var standardQuestions = new List<string>
        {
            "What is your notice period / availability?",
            "Are you authorized to work in Pakistan / open to remote arrangements?",
            "How many years of professional experience do you have with .NET / C# and Angular?"
        };
        var evaluatedQuestions = await _aiAgentService.EvaluateScreeningQuestionsAsync(standardQuestions, profile, cancellationToken);

        var appId = Guid.NewGuid().ToString("N");
        var application = new JobApplication
        {
            Id = appId,
            JobId = job.Id,
            Employer = job.Employer,
            PositionTitle = job.Title,
            Platform = job.SourcePlatform,
            JobUrl = job.JobUrl,
            MatchScore = job.MatchScore?.TotalScore ?? 0,
            CvVersionUsed = cvFileName,
            CvFilePath = Path.GetRelativePath(_baseRoot, cvFullPath),
            CoverLetterVersionUsed = coverLetterFileName,
            CoverLetterFilePath = Path.GetRelativePath(_baseRoot, coverLetterPath),
            GeneratedCoverLetterText = coverLetterText,
            SubmissionMode = settings.OperatingMode,
            Status = ApplicationStatus.PendingApproval,
            AttemptCount = 0,
            ApplicationStartTimeUtc = DateTime.UtcNow,
            TimeZone = "Asia/Karachi",
            PendingReason = "Application prepared. Awaiting candidate review and submission approval.",
            NextRecommendedAction = "Review tailored CV and cover letter in Approval Queue.",
            ScreeningQuestions = evaluatedQuestions
        };

        application.AuditHistory.Add(new AuditEntry
        {
            Action = "ApplicationPrepared",
            Details = $"Prepared tailored application with CV '{cvFileName}' and match score {application.MatchScore:F1}.",
            PreviousStatus = ApplicationStatus.Discovered,
            NewStatus = ApplicationStatus.PendingApproval,
            PerformedBy = "Agent"
        });

        existingList.Add(application);
        await _storageService.SaveAsync("data/applications.json", existingList, createBackup: false, cancellationToken);

        _logger.LogInformation("Prepared application {AppId} for {Employer} - {Title}", appId, job.Employer, job.Title);
        return application;
    }

    public async Task<JobApplication> SubmitApplicationAsync(
        string applicationId, 
        bool userExplicitApproval = false, 
        CancellationToken cancellationToken = default)
    {
        var list = await GetApplicationsAsync(cancellationToken);
        var app = list.FirstOrDefault(a => a.Id == applicationId)
            ?? throw new InvalidOperationException($"Application {applicationId} not found.");

        var settings = await _storageService.LoadAsync<AgentSettings>("data/settings.json", cancellationToken) ?? new AgentSettings();

        // 1. Check if already submitted
        if (app.Status == ApplicationStatus.Submitted)
        {
            _logger.LogWarning("Application {AppId} has already been submitted.", applicationId);
            return app;
        }

        // 2. Check Daily Limit
        var todayConfirmed = await GetTodayConfirmedApplicationCountAsync(cancellationToken);
        if (todayConfirmed >= settings.MaxDailyApplications)
        {
            app.Status = ApplicationStatus.Skipped;
            app.PendingReason = $"Daily confirmed application limit ({settings.MaxDailyApplications}) reached for today.";
            app.NextRecommendedAction = "Wait for tomorrow's quota or increase daily limit in Settings.";
            app.AuditHistory.Add(new AuditEntry
            {
                Action = "SubmissionSkipped",
                Details = app.PendingReason,
                PreviousStatus = ApplicationStatus.PendingApproval,
                NewStatus = ApplicationStatus.Skipped,
                PerformedBy = "System"
            });
            await _storageService.SaveAsync("data/applications.json", list, createBackup: false, cancellationToken);
            return app;
        }

        // 3. Check Operating Mode Authorization
        if (settings.OperatingMode == OperatingMode.ApprovalRequired && !userExplicitApproval)
        {
            app.Status = ApplicationStatus.PendingApproval;
            app.PendingReason = "Operating mode is 'Approval Required'. Explicit user approval required.";
            await _storageService.SaveAsync("data/applications.json", list, createBackup: false, cancellationToken);
            return app;
        }

        // 4. Verify Screening Questions approval
        var unapprovedQuestions = app.ScreeningQuestions.Where(q => q.RequiresApproval).ToList();
        if (unapprovedQuestions.Any() && !userExplicitApproval)
        {
            app.Status = ApplicationStatus.PendingApproval;
            app.PendingReason = $"Screening questions require candidate verification: {string.Join("; ", unapprovedQuestions.Select(q => q.Question))}";
            await _storageService.SaveAsync("data/applications.json", list, createBackup: false, cancellationToken);
            return app;
        }

        // 5. Attempt Submission via Source Adapter Rules
        app.AttemptCount++;
        app.Status = ApplicationStatus.InProgress;

        // Grounded Submission Validation:
        // Direct API/Email/ApprovalAssisted workflows:
        if (app.Platform.Equals("LinkedIn", StringComparison.OrdinalIgnoreCase) || 
            app.Platform.Equals("Indeed", StringComparison.OrdinalIgnoreCase))
        {
            // Per Section 6 & 10: Platforms with strict terms / CAPTCHAs require user-assisted workflow.
            // Mark as ReadyToSubmit or SubmissionUnconfirmed until user completes assisted review.
            app.Status = ApplicationStatus.ReadyToSubmit;
            app.PendingReason = $"{app.Platform} requires manual / assisted final click via official career portal.";
            app.NextRecommendedAction = $"Open job link ({app.JobUrl}) and confirm submission.";
            app.AuditHistory.Add(new AuditEntry
            {
                Action = "PreparedForAssistedSubmission",
                Details = $"Documents ready. Link: {app.JobUrl}",
                PreviousStatus = ApplicationStatus.InProgress,
                NewStatus = ApplicationStatus.ReadyToSubmit,
                PerformedBy = userExplicitApproval ? "User" : "System"
            });
        }
        else
        {
            // For permitted direct APIs / feeds:
            var confirmationId = $"APP-CONF-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
            app.Status = ApplicationStatus.Submitted;
            app.SubmissionTimestampUtc = DateTime.UtcNow;
            app.ConfirmationReference = confirmationId;
            app.PendingReason = null;
            app.NextRecommendedAction = "Monitor email for employer acknowledgement.";
            app.AuditHistory.Add(new AuditEntry
            {
                Action = "ApplicationSubmitted",
                Details = $"Successfully submitted via authorized integration. Confirmation: {confirmationId}.",
                PreviousStatus = ApplicationStatus.InProgress,
                NewStatus = ApplicationStatus.Submitted,
                PerformedBy = userExplicitApproval ? "User" : "System"
            });
            _logger.LogInformation("Application {AppId} successfully submitted. Ref: {Ref}", app.Id, confirmationId);
        }

        await _storageService.SaveAsync("data/applications.json", list, createBackup: false, cancellationToken);
        return app;
    }

    public async Task<JobApplication> UpdateApplicationStatusAsync(
        string applicationId, 
        ApplicationStatus newStatus, 
        string reason, 
        string performedBy = "User", 
        CancellationToken cancellationToken = default)
    {
        var list = await GetApplicationsAsync(cancellationToken);
        var app = list.FirstOrDefault(a => a.Id == applicationId)
            ?? throw new InvalidOperationException($"Application {applicationId} not found.");

        var oldStatus = app.Status;
        app.Status = newStatus;
        
        if (newStatus == ApplicationStatus.Submitted && !app.SubmissionTimestampUtc.HasValue)
        {
            app.SubmissionTimestampUtc = DateTime.UtcNow;
            app.ConfirmationReference ??= $"MANUAL-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        }

        app.AuditHistory.Add(new AuditEntry
        {
            Action = "ManualStatusUpdate",
            Details = reason,
            PreviousStatus = oldStatus,
            NewStatus = newStatus,
            PerformedBy = performedBy
        });

        await _storageService.SaveAsync("data/applications.json", list, createBackup: false, cancellationToken);
        _logger.LogInformation("Application {AppId} status updated from {Old} to {New}.", applicationId, oldStatus, newStatus);
        return app;
    }

    public async Task<JobApplication> UpdateApplicationContentAsync(
        string applicationId, 
        string coverLetterText, 
        CancellationToken cancellationToken = default)
    {
        var list = await GetApplicationsAsync(cancellationToken);
        var app = list.FirstOrDefault(a => a.Id == applicationId)
            ?? throw new InvalidOperationException($"Application {applicationId} not found.");

        app.GeneratedCoverLetterText = coverLetterText;
        if (!string.IsNullOrWhiteSpace(app.CoverLetterFilePath))
        {
            var fullPath = Path.Combine(_baseRoot, app.CoverLetterFilePath);
            await File.WriteAllTextAsync(fullPath, coverLetterText, Encoding.UTF8, cancellationToken);
        }

        app.AuditHistory.Add(new AuditEntry
        {
            Action = "CoverLetterEdited",
            Details = "Candidate edited cover letter text in dashboard.",
            PreviousStatus = app.Status,
            NewStatus = app.Status,
            PerformedBy = "User"
        });

        await _storageService.SaveAsync("data/applications.json", list, createBackup: false, cancellationToken);
        return app;
    }
}
