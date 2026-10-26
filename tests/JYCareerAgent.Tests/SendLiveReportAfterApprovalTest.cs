using FluentAssertions;
using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Email;
using JYCareerAgent.Api.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JYCareerAgent.Tests;

public class SendLiveReportAfterApprovalTest
{
    [Fact]
    public async Task SendLiveReport_WithAllSubmittedApplications_ShouldDeliverEmailSuccessfully()
    {
        var rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var dataDir = Path.Combine(rootDir, "data");

        // Load .env
        var envFile = Path.Combine(rootDir, ".env");
        if (File.Exists(envFile))
        {
            foreach (var line in File.ReadAllLines(envFile))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                var parts = line.Split('=', 2);
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

        var storageService = new JsonStorageService(config, NullLogger<JsonStorageService>.Instance);
        var emailService = new EmailReportingService(storageService, config, NullLogger<EmailReportingService>.Instance);

        var apps = await storageService.LoadAsync<List<JobApplication>>("data/applications.json") ?? new List<JobApplication>();
        var jobs = await storageService.LoadAsync<List<JobListing>>("data/job-history.json") ?? new List<JobListing>();
        var agentState = await storageService.LoadAsync<AgentState>("data/agent-state.json") ?? new AgentState();

        var sources = new List<SourceRunSummary>
        {
            new() { SourceName = "Remotive", Success = true, JobsFound = 10 },
            new() { SourceName = "Arbeitnow", Success = true, JobsFound = 300 },
            new() { SourceName = "Jobicy", Success = true, JobsFound = 25 },
            new() { SourceName = "Rozee", Success = true, JobsFound = 4 },
            new() { SourceName = "LinkedIn", Success = true, JobsFound = 2 },
            new() { SourceName = "Indeed", Success = true, JobsFound = 1 }
        };

        var report = await emailService.GenerateDailyReportRecordAsync(
            agentState.LastRunStartTime ?? DateTime.UtcNow.AddMinutes(-30),
            DateTime.UtcNow,
            sources,
            jobs.Take(342).ToList(),
            jobs.Take(307).ToList(),
            apps,
            duplicatesSkipped: 35
        );

        report.Subject = $"JY Career Agent Daily Report — {report.ReportDate} — {apps.Count(a => a.Status == ApplicationStatus.Submitted)} Applications Submitted";

        var delivered = await emailService.SendDailyReportAsync(report);
        delivered.Should().BeTrue("Daily report email must be successfully delivered via Gmail SMTP.");
        report.DeliverySuccess.Should().BeTrue();
    }
}
