using FluentAssertions;
using JYCareerAgent.Api.Controllers;
using JYCareerAgent.Api.Services.AI;
using JYCareerAgent.Api.Services.Applications;
using JYCareerAgent.Api.Services.CV;
using JYCareerAgent.Api.Services.Email;
using JYCareerAgent.Api.Services.JobSources;
using JYCareerAgent.Api.Services.Matching;
using JYCareerAgent.Api.Services.Scheduling;
using JYCareerAgent.Api.Services.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JYCareerAgent.Tests;

public class FakeAppLifetime : IHostApplicationLifetime
{
    public CancellationToken ApplicationStarted => CancellationToken.None;
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;
    public void StopApplication() { }
}

[Collection("SchedulerLock")]
public class SchedulerEndpointTests : IDisposable
{
    private readonly string _testDir;
    private readonly FakeAppLifetime _lifetime;

    public SchedulerEndpointTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "jy_scheduler_endpoint_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _lifetime = new FakeAppLifetime();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true);
        }
        catch { }
    }

    private (SchedulerController schedulerCtrl, DashboardController dashboardCtrl, AgentOrchestrator orchestrator) CreateControllers(string? secret = null)
    {
        var inMemory = new Dictionary<string, string?>
        {
            { "JY_STORAGE_ROOT", _testDir }
        };

        if (secret != null)
        {
            inMemory["JY_SCHEDULER_SECRET"] = secret;
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemory)
            .Build();

        var storage = new JsonStorageService(config, NullLogger<JsonStorageService>.Instance);
        var factory = new JobSourceFactory(new List<IJobSource>(), NullLogger<JobSourceFactory>.Instance);
        var norm = new JobNormalizationService();
        var match = new JobMatchingEngine(NullLogger<JobMatchingEngine>.Instance);
        var cv = new CvManagerService(storage, config, NullLogger<CvManagerService>.Instance);
        var ai = new AiAgentService(new HttpClient(), config, NullLogger<AiAgentService>.Instance);
        var workflow = new ApplicationWorkflowService(storage, cv, ai, config, NullLogger<ApplicationWorkflowService>.Instance);
        var email = new EmailReportingService(storage, config, NullLogger<EmailReportingService>.Instance);
        var orchestrator = new AgentOrchestrator(storage, factory, norm, match, cv, workflow, email, NullLogger<AgentOrchestrator>.Instance);

        var schedulerCtrl = new SchedulerController(
            orchestrator,
            storage,
            config,
            NullLogger<SchedulerController>.Instance,
            _lifetime);
        schedulerCtrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var dashboardCtrl = new DashboardController(
            storage,
            orchestrator,
            workflow,
            factory,
            email,
            _lifetime,
            NullLogger<DashboardController>.Instance,
            config);
        dashboardCtrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return (schedulerCtrl, dashboardCtrl, orchestrator);
    }

    private async Task WaitForOrchestratorIdleAsync(AgentOrchestrator orchestrator)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (orchestrator.IsRunning && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task SchedulerTrigger_WhenSecretNotConfigured_AcceptsRequest()
    {
        var (schedulerCtrl, _, orchestrator) = CreateControllers(secret: null);
        await WaitForOrchestratorIdleAsync(orchestrator);

        var result = schedulerCtrl.TriggerScheduledRun(headerKey: null, key: null);

        result.Should().BeOfType<AcceptedResult>();
        var accepted = (AcceptedResult)result;
        accepted.StatusCode.Should().Be(202);
        await WaitForOrchestratorIdleAsync(orchestrator);
    }

    [Fact]
    public async Task SchedulerTrigger_WhenSecretConfigured_AndKeyMissing_ReturnsUnauthorized()
    {
        const string secret = "super-secret-trigger-key";
        var (schedulerCtrl, _, orchestrator) = CreateControllers(secret: secret);
        await WaitForOrchestratorIdleAsync(orchestrator);

        var result = schedulerCtrl.TriggerScheduledRun(headerKey: null, key: null);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        var unauthorized = (UnauthorizedObjectResult)result;
        unauthorized.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task SchedulerTrigger_WhenSecretConfigured_AndWrongKey_ReturnsUnauthorized()
    {
        const string secret = "super-secret-trigger-key";
        var (schedulerCtrl, _, orchestrator) = CreateControllers(secret: secret);
        await WaitForOrchestratorIdleAsync(orchestrator);

        var result = schedulerCtrl.TriggerScheduledRun(headerKey: "wrong-secret", key: null);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task SchedulerTrigger_WhenSecretConfigured_AndValidHeaderKey_AcceptsRequest()
    {
        const string secret = "super-secret-trigger-key";
        var (schedulerCtrl, _, orchestrator) = CreateControllers(secret: secret);
        await WaitForOrchestratorIdleAsync(orchestrator);

        var result = schedulerCtrl.TriggerScheduledRun(headerKey: secret, key: null);

        result.Should().BeOfType<AcceptedResult>();
        var accepted = (AcceptedResult)result;
        accepted.StatusCode.Should().Be(202);
        await WaitForOrchestratorIdleAsync(orchestrator);
    }

    [Fact]
    public async Task SchedulerTrigger_WhenSecretConfigured_AndValidBearerToken_AcceptsRequest()
    {
        const string secret = "super-secret-trigger-key";
        var (schedulerCtrl, _, orchestrator) = CreateControllers(secret: secret);
        await WaitForOrchestratorIdleAsync(orchestrator);
        schedulerCtrl.HttpContext.Request.Headers.Authorization = "Bearer " + secret;

        var result = schedulerCtrl.TriggerScheduledRun(headerKey: null, key: null);

        result.Should().BeOfType<AcceptedResult>();
        await WaitForOrchestratorIdleAsync(orchestrator);
    }

    [Fact]
    public async Task DashboardRunNow_WhenSecretConfigured_AndValidHeaderKey_AcceptsRequest()
    {
        const string secret = "super-secret-trigger-key";
        var (_, dashboardCtrl, orchestrator) = CreateControllers(secret: secret);
        await WaitForOrchestratorIdleAsync(orchestrator);

        var result = dashboardCtrl.TriggerManualRun(headerKey: secret, key: null);

        result.Should().BeOfType<AcceptedResult>();
        var accepted = (AcceptedResult)result;
        accepted.StatusCode.Should().Be(202);
        await WaitForOrchestratorIdleAsync(orchestrator);
    }

    [Fact]
    public async Task DashboardRunNow_WhenSecretConfigured_AndWrongKey_ReturnsUnauthorized()
    {
        const string secret = "super-secret-trigger-key";
        var (_, dashboardCtrl, orchestrator) = CreateControllers(secret: secret);
        await WaitForOrchestratorIdleAsync(orchestrator);

        var result = dashboardCtrl.TriggerManualRun(headerKey: "wrong-secret", key: null);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }
}
