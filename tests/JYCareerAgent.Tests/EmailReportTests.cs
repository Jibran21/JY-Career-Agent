using FluentAssertions;
using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Email;
using JYCareerAgent.Api.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JYCareerAgent.Tests;

[Collection("SchedulerLock")]
public class EmailReportTests : IDisposable
{
    private readonly string _testDir;
    private readonly JsonStorageService _storageService;
    private readonly EmailReportingService _emailService;

    public EmailReportTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "jy_email_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "JY_STORAGE_ROOT", _testDir }
            })
            .Build();

        _storageService = new JsonStorageService(config, NullLogger<JsonStorageService>.Instance);
        _emailService = new EmailReportingService(_storageService, config, NullLogger<EmailReportingService>.Instance);
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
    public async Task GenerateDailyReportRecord_ShouldProduceCorrectMetricsAndRequiredSections()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddMinutes(-5);
        var endTime = DateTime.UtcNow;
        var sources = new List<SourceRunSummary>
        {
            new() { SourceName = "Remotive", Success = true, JobsFound = 12 },
            new() { SourceName = "Rozee", Success = true, JobsFound = 4 }
        };
        var allJobs = new List<JobListing> { new(), new() };
        var uniqueJobs = new List<JobListing>
        {
            new() { MatchScore = new MatchScoreResult { TotalScore = 85 } },
            new() { MatchScore = new MatchScoreResult { TotalScore = 70 } }
        };

        var app1 = new JobApplication
        {
            Employer = "Systems Limited",
            PositionTitle = "Senior .NET Developer",
            Status = ApplicationStatus.Submitted,
            SubmissionTimestampUtc = DateTime.UtcNow,
            ConfirmationReference = "CONF-12345"
        };
        var app2 = new JobApplication
        {
            Employer = "Aion Digital",
            PositionTitle = "Full Stack Engineer",
            Status = ApplicationStatus.PendingApproval,
            PendingReason = "Awaiting review"
        };
        var apps = new List<JobApplication> { app1, app2 };

        // Act
        var report = await _emailService.GenerateDailyReportRecordAsync(
            startTime, endTime, sources, allJobs, uniqueJobs, apps, duplicatesSkipped: 3);

        // Assert
        report.Subject.Should().Contain("1 Applications Submitted");
        report.TotalJobsDiscovered.Should().Be(2);
        report.HighMatchJobsCount.Should().Be(1);
        report.ApplicationsSubmittedCount.Should().Be(1);
        report.ApplicationsPendingCount.Should().Be(1);
        report.DuplicatesSkippedCount.Should().Be(3);

        report.ConfirmedSubmissions.Should().HaveCount(1);
        report.PendingApprovals.Should().HaveCount(1);

        // Verify HTML contents
        report.HtmlBody.Should().Contain("JY Career Agent — Daily Execution Report");
        report.HtmlBody.Should().Contain("Jibran Yousuf Khan");
        report.HtmlBody.Should().Contain("Confirmed Submissions");
        report.HtmlBody.Should().Contain("Systems Limited");
        report.HtmlBody.Should().Contain("CONF-12345");
        report.HtmlBody.Should().Contain("http://localhost:4200");

        // Verify secrets are NOT exposed anywhere in report
        report.HtmlBody.Should().NotContain("password", "Emails should never contain passwords");
    }

    [Fact]
    public async Task SendDailyReportAsync_WhenSmtpPasswordMissing_ShouldRecordLocalNoticeWithoutCrashing()
    {
        // Arrange
        var prevEnv1 = Environment.GetEnvironmentVariable("JY_EMAIL__SMTP_PASSWORD");
        var prevEnv2 = Environment.GetEnvironmentVariable("JY_EMAIL:SMTP_PASSWORD");

        try
        {
            Environment.SetEnvironmentVariable("JY_EMAIL__SMTP_PASSWORD", "");
            Environment.SetEnvironmentVariable("JY_EMAIL:SMTP_PASSWORD", "");

            var settings = new AgentSettings { Email = new EmailSettings { DailyReportEnabled = true } };
            await _storageService.SaveAsync("data/settings.json", settings);

            var report = new EmailReportRecord
            {
                Subject = "Test Subject",
                HtmlBody = "<html>Test</html>",
                PlainTextBody = "Test"
            };

            // Act
            var result = await _emailService.SendDailyReportAsync(report);

            // Assert
            result.Should().BeFalse();
            report.DeliveryStatusMessage.Should().Contain("SMTP Password not configured");

            // Report should still be persisted to history
            var history = await _emailService.GetReportHistoryAsync();
            history.Should().HaveCount(1);
            history[0].DeliverySuccess.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("JY_EMAIL__SMTP_PASSWORD", prevEnv1);
            Environment.SetEnvironmentVariable("JY_EMAIL:SMTP_PASSWORD", prevEnv2);
        }
    }
}
