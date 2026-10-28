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
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace JYCareerAgent.Tests;

[Collection("SchedulerLock")]
public class SafeLiveSchedulerRunTest
{
    private readonly ITestOutputHelper _output;

    public SafeLiveSchedulerRunTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SafeLiveSchedulerTestRun_ShouldCompleteWorkflow_AndSendSummaryEmail()
    {
        var rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        // Load .env credentials if present
        var envFile = Path.Combine(rootDir, ".env");
        if (File.Exists(envFile))
        {
            foreach (var line in File.ReadAllLines(envFile))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;
                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2)
                {
                    Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
                }
            }
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "JY_STORAGE_ROOT", rootDir }
            })
            .AddEnvironmentVariables()
            .Build();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Information);
        });

        var storageService = new JsonStorageService(config, loggerFactory.CreateLogger<JsonStorageService>());
        var normService = new JobNormalizationService();
        var matchEngine = new JobMatchingEngine(loggerFactory.CreateLogger<JobMatchingEngine>());
        var cvService = new CvManagerService(storageService, config, loggerFactory.CreateLogger<CvManagerService>());
        var aiService = new AiAgentService(new HttpClient(), config, loggerFactory.CreateLogger<AiAgentService>());
        var workflowService = new ApplicationWorkflowService(storageService, cvService, aiService, config, loggerFactory.CreateLogger<ApplicationWorkflowService>());
        var emailService = new EmailReportingService(storageService, config, loggerFactory.CreateLogger<EmailReportingService>());

        var sources = new List<IJobSource>
        {
            new RemotiveJobSource(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, loggerFactory.CreateLogger<RemotiveJobSource>()),
            new ArbeitnowJobSource(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, loggerFactory.CreateLogger<ArbeitnowJobSource>()),
            new JobicyJobSource(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, loggerFactory.CreateLogger<JobicyJobSource>()),
            new RozeeJobSource(new HttpClient(), loggerFactory.CreateLogger<RozeeJobSource>()),
            new LinkedInJobSource(loggerFactory.CreateLogger<LinkedInJobSource>()),
            new IndeedJobSource(loggerFactory.CreateLogger<IndeedJobSource>())
        };
        var factory = new JobSourceFactory(sources, loggerFactory.CreateLogger<JobSourceFactory>());

        var orchestrator = new AgentOrchestrator(
            storageService,
            factory,
            normService,
            matchEngine,
            cvService,
            workflowService,
            emailService,
            loggerFactory.CreateLogger<AgentOrchestrator>());

        // 1. Verify operating mode in settings (Automatic mode enabled)
        var settings = await storageService.LoadAsync<AgentSettings>("data/settings.json") ?? new AgentSettings();
        settings.OperatingMode.Should().Be(OperatingMode.Automatic, "Operating mode is configured for Automatic daily application execution.");

        Environment.SetEnvironmentVariable("JY_SAFE_TEST_MODE", "true");
        try
        {
            _output.WriteLine("================================================================================");
            _output.WriteLine("LAUNCHING SAFE SCHEDULER TEST RUN (Trigger: SafeLiveVerificationTest)...");
            _output.WriteLine("================================================================================");

            // 2. Execute safe verification run
            var success = await orchestrator.RunCycleAsync("SafeLiveVerificationTest");

        // 3. Assertions
        success.Should().BeTrue("The orchestrator execution cycle must complete successfully.");

        // Check Agent State
        var agentState = await storageService.LoadAsync<AgentState>("data/agent-state.json");
        agentState.Should().NotBeNull();
        agentState!.Status.Should().Be("Idle");
        agentState.LastRunStatus.Should().Be("Completed");
        agentState.TotalJobsFoundToday.Should().BeGreaterThan(0);

        _output.WriteLine($"Cycle Status:           {agentState.Status}");
        _output.WriteLine($"Last Run Status:        {agentState.LastRunStatus}");
        _output.WriteLine($"Raw Jobs Found Today:   {agentState.TotalJobsFoundToday}");
        _output.WriteLine($"Unique Jobs Found:      {agentState.UniqueJobsFoundToday}");
        _output.WriteLine($"Eligible Matches:       {agentState.MatchedJobsToday}");
        _output.WriteLine($"Applications Pending:   {agentState.ApplicationsPendingToday}");
        _output.WriteLine($"Next Scheduled Run:     {agentState.NextScheduledRun:yyyy-MM-dd HH:mm:ss} UTC");

        // Check Email Reports
        var reports = await storageService.LoadAsync<List<EmailReportRecord>>("data/email-reports.json");
        reports.Should().NotBeNullOrEmpty();
        var latestReport = reports!.First();
        _output.WriteLine($"Latest Email Report ID: {latestReport.Id}");
        _output.WriteLine($"Recipient:              {latestReport.RecipientEmail}");
        _output.WriteLine($"Delivery Success:       {latestReport.DeliverySuccess}");
        _output.WriteLine($"Delivery Message:       {latestReport.DeliveryStatusMessage}");

        latestReport.DeliverySuccess.Should().BeTrue("Daily report summary email must be delivered successfully to configured recipient.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JY_SAFE_TEST_MODE", null);
        }
    }
}
