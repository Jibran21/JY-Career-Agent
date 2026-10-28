using FluentAssertions;
using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.AI;
using JYCareerAgent.Api.Services.Applications;
using JYCareerAgent.Api.Services.CV;
using JYCareerAgent.Api.Services.Email;
using JYCareerAgent.Api.Services.JobSources;
using JYCareerAgent.Api.Services.Matching;
using JYCareerAgent.Api.Services.Scheduling;
using JYCareerAgent.Api.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JYCareerAgent.Tests;

[Collection("SchedulerLock")]
public class FullSimulationTests : IDisposable
{
    private readonly string _testDir;
    private readonly JsonStorageService _storage;
    private readonly AgentOrchestrator _orchestrator;
    private readonly IEmailReportingService _emailService;

    public FullSimulationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "jy_sim_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(Path.Combine(_testDir, "data"));
        Directory.CreateDirectory(Path.Combine(_testDir, "cv", "master-cv"));
        Directory.CreateDirectory(Path.Combine(_testDir, "cv", "tailored"));
        Directory.CreateDirectory(Path.Combine(_testDir, "cover-letters"));
        Directory.CreateDirectory(Path.Combine(_testDir, "prompts"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "JY_STORAGE_ROOT", _testDir }
            })
            .Build();

        _storage = new JsonStorageService(config, NullLogger<JsonStorageService>.Instance);

        var norm = new JobNormalizationService();
        var matchEngine = new JobMatchingEngine(NullLogger<JobMatchingEngine>.Instance);
        var cvService = new CvManagerService(_storage, config, NullLogger<CvManagerService>.Instance);
        var aiService = new AiAgentService(new HttpClient(), config, NullLogger<AiAgentService>.Instance);
        var workflow = new ApplicationWorkflowService(_storage, cvService, aiService, config, NullLogger<ApplicationWorkflowService>.Instance);
        _emailService = new EmailReportingService(_storage, config, NullLogger<EmailReportingService>.Instance);

        var sources = new List<IJobSource>
        {
            new RozeeJobSource(new HttpClient(), NullLogger<RozeeJobSource>.Instance),
            new LinkedInJobSource(NullLogger<LinkedInJobSource>.Instance),
            new IndeedJobSource(NullLogger<IndeedJobSource>.Instance)
        };
        var factory = new JobSourceFactory(sources, NullLogger<JobSourceFactory>.Instance);

        _orchestrator = new AgentOrchestrator(
            _storage,
            factory,
            norm,
            matchEngine,
            cvService,
            workflow,
            _emailService,
            NullLogger<AgentOrchestrator>.Instance);
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
    public async Task RunCycleAsync_ShouldExecuteFullPipeline_AndGenerateEmailReport()
    {
        // 1. Initialize verified profile and default settings in test directory
        var profile = new CandidateProfile
        {
            FullName = "Jibran Yousuf Khan",
            Headline = "Data & Software Engineer | .NET • Angular • Data • AI",
            Location = "Karachi, Pakistan",
            Email = "jibrankhalily@gmail.com",
            Skills = new SkillCatalog
            {
                LanguagesAndFrameworks = new List<string> { "C#", ".NET Core", "ASP.NET Core", "Angular", "TypeScript" },
                DatabaseAndReporting = new List<string> { "MS SQL Server", "SSIS", "Power BI" },
                ArchitectureAndMessaging = new List<string> { "Clean Architecture", "SignalR", "RESTful APIs" }
            },
            Experience = new List<WorkExperience>
            {
                new() { Company = "Think Enabled", Title = "Senior Consultant / Senior Software Engineer", Period = "Jan 2024 – Present", Location = "Karachi, Pakistan" },
                new() { Company = "Aion Digital", Title = "Software Engineer", Period = "Mar 2021 – Dec 2023", Location = "Karachi, Pakistan" }
            }
        };
        await _storage.SaveAsync("data/profile.json", profile);

        var settings = new AgentSettings
        {
            OperatingMode = OperatingMode.ApprovalRequired,
            MaxDailyApplications = 10,
            Email = new EmailSettings { DailyReportEnabled = true, RecipientEmail = "jibrankhalily@gmail.com" }
        };
        await _storage.SaveAsync("data/settings.json", settings);
        await _storage.SaveAsync("data/agent-state.json", new AgentState());
        await _storage.SaveAsync("data/job-history.json", new List<JobListing>());
        await _storage.SaveAsync("data/applications.json", new List<JobApplication>());
        await _storage.SaveAsync("data/email-reports.json", new List<EmailReportRecord>());

        // 2. Act - Run execution cycle
        var success = await _orchestrator.RunCycleAsync("UnitTestSimulatedDailyRun");

        // 3. Assert
        success.Should().BeTrue();

        // Verify Agent State
        var state = await _storage.LoadAsync<AgentState>("data/agent-state.json");
        state.Should().NotBeNull();
        state!.Status.Should().Be("Idle");
        state.LastRunStatus.Should().Be("Completed");
        state.TotalJobsFoundToday.Should().BeGreaterThan(0);

        // Verify Job History was populated and scored
        var history = await _storage.LoadAsync<List<JobListing>>("data/job-history.json");
        history.Should().NotBeNullOrEmpty();
        history!.All(j => j.MatchScore != null).Should().BeTrue("All jobs must be evaluated with match scores");

        // Verify Applications were prepared
        var applications = await _storage.LoadAsync<List<JobApplication>>("data/applications.json");
        applications.Should().NotBeNullOrEmpty();
        applications!.All(a => a.Status == ApplicationStatus.PendingApproval).Should().BeTrue("Mode A requires approval");
        applications.All(a => !string.IsNullOrWhiteSpace(a.GeneratedCoverLetterText)).Should().BeTrue("All prepared applications should have cover letters");

        // Verify Email Report was generated
        var reports = await _storage.LoadAsync<List<EmailReportRecord>>("data/email-reports.json");
        reports.Should().NotBeNullOrEmpty();
        var report = reports!.First();
        report.Subject.Should().Contain("JY Career Agent Daily Report");
        report.TotalJobsDiscovered.Should().Be(history.Count);
        report.HtmlBody.Should().Contain("Jibran Yousuf Khan");
    }
}
