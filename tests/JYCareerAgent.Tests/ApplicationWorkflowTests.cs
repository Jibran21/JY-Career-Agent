using FluentAssertions;
using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.AI;
using JYCareerAgent.Api.Services.Applications;
using JYCareerAgent.Api.Services.CV;
using JYCareerAgent.Api.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JYCareerAgent.Tests;

[Collection("SchedulerLock")]
public class ApplicationWorkflowTests : IDisposable
{
    private readonly string _testDir;
    private readonly JsonStorageService _storageService;
    private readonly CvManagerService _cvManagerService;
    private readonly AiAgentService _aiAgentService;
    private readonly ApplicationWorkflowService _workflowService;

    public ApplicationWorkflowTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "jy_workflow_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "JY_STORAGE_ROOT", _testDir }
            })
            .Build();

        _storageService = new JsonStorageService(config, NullLogger<JsonStorageService>.Instance);
        _cvManagerService = new CvManagerService(_storageService, config, NullLogger<CvManagerService>.Instance);
        _aiAgentService = new AiAgentService(new HttpClient(), config, NullLogger<AiAgentService>.Instance);
        _workflowService = new ApplicationWorkflowService(_storageService, _cvManagerService, _aiAgentService, config, NullLogger<ApplicationWorkflowService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true);
        }
        catch { }
    }

    [Fact]
    public async Task PrepareApplication_ShouldInitializeInPendingApprovalStatus()
    {
        var job = new JobListing { Id = "test-job-1", Employer = "Think Enabled", Title = "Senior .NET Engineer", SourcePlatform = "Rozee" };
        var profile = new CandidateProfile { FullName = "Jibran Yousuf Khan" };
        var settings = new AgentSettings { OperatingMode = OperatingMode.ApprovalRequired };

        var app = await _workflowService.PrepareApplicationAsync(job, profile, settings);

        app.Status.Should().Be(ApplicationStatus.PendingApproval);
        app.AuditHistory.Should().NotBeEmpty();
        app.GeneratedCoverLetterText.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SubmitApplication_InApprovalRequiredMode_WithoutExplicitUserConsent_ShouldNotSubmit()
    {
        var job = new JobListing { Id = "test-job-2", Employer = "Aion Digital", Title = "Software Engineer", SourcePlatform = "Rozee" };
        var profile = new CandidateProfile { FullName = "Jibran Yousuf Khan" };
        var settings = new AgentSettings { OperatingMode = OperatingMode.ApprovalRequired };
        await _storageService.SaveAsync("data/settings.json", settings);

        var app = await _workflowService.PrepareApplicationAsync(job, profile, settings);

        // Act - Attempt submit without explicit approval
        var result = await _workflowService.SubmitApplicationAsync(app.Id, userExplicitApproval: false);

        // Assert - Remains pending approval
        result.Status.Should().Be(ApplicationStatus.PendingApproval);
        result.SubmissionTimestampUtc.Should().BeNull();
    }

    [Fact]
    public async Task SubmitApplication_WithUserApproval_ShouldSubmitAndAssignConfirmationReference()
    {
        var job = new JobListing { Id = "test-job-3", Employer = "Cognitive Solutions", Title = "AI Engineer", SourcePlatform = "Remotive" };
        var profile = new CandidateProfile { FullName = "Jibran Yousuf Khan" };
        var settings = new AgentSettings { OperatingMode = OperatingMode.ApprovalRequired };
        await _storageService.SaveAsync("data/settings.json", settings);

        var app = await _workflowService.PrepareApplicationAsync(job, profile, settings);

        // Act
        var result = await _workflowService.SubmitApplicationAsync(app.Id, userExplicitApproval: true);

        // Assert
        result.Status.Should().Be(ApplicationStatus.Submitted);
        result.ConfirmationReference.Should().StartWith("APP-CONF-");
        result.SubmissionTimestampUtc.Should().NotBeNull();
        result.GetSubmissionTimeKarachi().Should().Contain("PKT");
    }

    [Fact]
    public async Task SubmitApplication_WhenDailyLimitReached_ShouldMarkAsSkipped()
    {
        var settings = new AgentSettings { MaxDailyApplications = 1, OperatingMode = OperatingMode.ApprovalRequired };
        await _storageService.SaveAsync("data/settings.json", settings);

        // Simulate 1 already submitted today
        var app1 = new JobApplication
        {
            Id = "app-1",
            Status = ApplicationStatus.Submitted,
            SubmissionTimestampUtc = DateTime.UtcNow
        };
        var app2 = new JobApplication
        {
            Id = "app-2",
            Status = ApplicationStatus.PendingApproval
        };
        await _storageService.SaveAsync("data/applications.json", new List<JobApplication> { app1, app2 });

        // Act - Attempt submit second application
        var result = await _workflowService.SubmitApplicationAsync("app-2", userExplicitApproval: true);

        // Assert
        result.Status.Should().Be(ApplicationStatus.Skipped);
        result.PendingReason.Should().Contain("Daily confirmed application limit");
    }

    [Fact]
    public async Task SubmitApplication_InAutomaticMode_OnDirectSupportedPlatform_ShouldAutomaticallySubmitWithoutUserApproval()
    {
        var job = new JobListing { Id = "test-job-auto", Employer = "RemoteDevs Co", Title = "Senior .NET Lead", SourcePlatform = "Remotive", JobUrl = "https://remotive.com/jobs/1" };
        var profile = new CandidateProfile { FullName = "Jibran Yousuf Khan" };
        var settings = new AgentSettings { OperatingMode = OperatingMode.Automatic, MaxDailyApplications = 50 };
        await _storageService.SaveAsync("data/settings.json", settings);

        var app = await _workflowService.PrepareApplicationAsync(job, profile, settings);

        // Act - Submit automatically with userExplicitApproval: false
        var result = await _workflowService.SubmitApplicationAsync(app.Id, userExplicitApproval: false);

        // Assert
        result.Status.Should().Be(ApplicationStatus.Submitted);
        result.ConfirmationReference.Should().StartWith("APP-CONF-");
        result.SubmissionTimestampUtc.Should().NotBeNull();
        result.AuditHistory.Should().Contain(a => a.Action == "ApplicationSubmitted");
    }

    [Fact]
    public async Task SubmitApplication_InAutomaticMode_OnProtectedPlatform_ShouldMarkAsManualActionRequired()
    {
        var job = new JobListing { Id = "test-job-linkedin", Employer = "BigTech Org", Title = "Senior Backend Engineer", SourcePlatform = "LinkedIn", JobUrl = "https://linkedin.com/jobs/view/123" };
        var profile = new CandidateProfile { FullName = "Jibran Yousuf Khan" };
        var settings = new AgentSettings { OperatingMode = OperatingMode.Automatic, MaxDailyApplications = 50 };
        await _storageService.SaveAsync("data/settings.json", settings);

        var app = await _workflowService.PrepareApplicationAsync(job, profile, settings);

        // Act - Attempt automated submission on LinkedIn
        var result = await _workflowService.SubmitApplicationAsync(app.Id, userExplicitApproval: false);

        // Assert - Must be marked ManualActionRequired, NOT bypassed or scraped illegally
        result.Status.Should().Be(ApplicationStatus.ManualActionRequired);
        result.PendingReason.Should().Contain("LinkedIn requires manual interaction");
        result.JobUrl.Should().Be("https://linkedin.com/jobs/view/123");
        result.AuditHistory.Should().Contain(a => a.Action == "PreparedForManualSubmission");
    }

    [Fact]
    public async Task SubmitApplication_InAutomaticMode_WithUnapprovedScreeningQuestions_ShouldMarkAsManualActionRequired()
    {
        var job = new JobListing { Id = "test-job-screening", Employer = "FinTech Global", Title = "C# Core Specialist", SourcePlatform = "Arbeitnow" };
        var profile = new CandidateProfile { FullName = "Jibran Yousuf Khan" };
        var settings = new AgentSettings { OperatingMode = OperatingMode.Automatic, MaxDailyApplications = 50 };
        await _storageService.SaveAsync("data/settings.json", settings);

        var app = await _workflowService.PrepareApplicationAsync(job, profile, settings);
        
        // Add an unapproved/unverified screening question
        app.ScreeningQuestions.Add(new ScreeningQuestionAnswer
        {
            Question = "Are you willing to relocate to Tokyo immediately?",
            RequiresApproval = true
        });
        var list = await _workflowService.GetApplicationsAsync();
        var existing = list.First(a => a.Id == app.Id);
        existing.ScreeningQuestions = app.ScreeningQuestions;
        await _storageService.SaveAsync("data/applications.json", list);

        // Act - Attempt automatic submission
        var result = await _workflowService.SubmitApplicationAsync(app.Id, userExplicitApproval: false);

        // Assert - Never fabricate answers; mark for manual candidate verification
        result.Status.Should().Be(ApplicationStatus.ManualActionRequired);
        result.PendingReason.Should().Contain("Screening questions require candidate verification");
    }

    [Fact]
    public async Task SubmitApplication_WhenSafeTestModeIsActive_ShouldHoldInPendingApproval()
    {
        Environment.SetEnvironmentVariable("JY_SAFE_TEST_MODE", "true");
        try
        {
            var job = new JobListing { Id = "test-job-safe", Employer = "SafeCorp", Title = ".NET Specialist", SourcePlatform = "Remotive" };
            var profile = new CandidateProfile { FullName = "Jibran Yousuf Khan" };
            var settings = new AgentSettings { OperatingMode = OperatingMode.Automatic, MaxDailyApplications = 50 };
            await _storageService.SaveAsync("data/settings.json", settings);

            var app = await _workflowService.PrepareApplicationAsync(job, profile, settings);

            // Act - In safe test mode, live applications must never fire
            var result = await _workflowService.SubmitApplicationAsync(app.Id, userExplicitApproval: false);

            // Assert
            result.Status.Should().Be(ApplicationStatus.PendingApproval);
            result.PendingReason.Should().Contain("Safe test simulation mode active");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JY_SAFE_TEST_MODE", null);
        }
    }
}
