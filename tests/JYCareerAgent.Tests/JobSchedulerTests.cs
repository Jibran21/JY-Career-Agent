using FluentAssertions;
using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Scheduling;
using Xunit;

namespace JYCareerAgent.Tests;

public class JobSchedulerTests
{
    [Fact]
    public void GetKarachiTimeZone_ShouldReturnUtcPlus5()
    {
        var tz = JobSchedulerBackgroundService.GetKarachiTimeZone();
        tz.Should().NotBeNull();
        tz.BaseUtcOffset.Should().Be(TimeSpan.FromHours(5), "Asia/Karachi is UTC+5");
    }

    [Fact]
    public void CalculateNextRun_WhenBeforeScheduledTime_ShouldScheduleForToday()
    {
        var karachiTz = JobSchedulerBackgroundService.GetKarachiTimeZone();
        var nowKarachi = new DateTime(2026, 10, 28, 8, 30, 0); // 8:30 AM PKT
        var scheduledTime = new TimeSpan(9, 0, 0); // 9:00 AM PKT
        DateTime? lastRunUtc = new DateTime(2026, 10, 27, 4, 0, 0, DateTimeKind.Utc); // Yesterday

        var (nextRunKarachi, isMissedCatchUp) = JobSchedulerBackgroundService.CalculateNextRun(
            nowKarachi, 
            scheduledTime, 
            lastRunUtc, 
            "Completed", 
            karachiTz);

        isMissedCatchUp.Should().BeFalse();
        nextRunKarachi.Should().Be(new DateTime(2026, 10, 28, 9, 0, 0));
    }

    [Fact]
    public void CalculateNextRun_WhenPastScheduledTimeAndNotRunToday_ShouldTriggerMissedCatchUp()
    {
        var karachiTz = JobSchedulerBackgroundService.GetKarachiTimeZone();
        var nowKarachi = new DateTime(2026, 10, 28, 10, 15, 0); // 10:15 AM PKT (container was asleep at 9 AM)
        var scheduledTime = new TimeSpan(9, 0, 0); // 9:00 AM PKT
        DateTime? lastRunUtc = new DateTime(2026, 10, 27, 4, 0, 0, DateTimeKind.Utc); // Yesterday

        var (nextRunKarachi, isMissedCatchUp) = JobSchedulerBackgroundService.CalculateNextRun(
            nowKarachi, 
            scheduledTime, 
            lastRunUtc, 
            "Completed", 
            karachiTz);

        isMissedCatchUp.Should().BeTrue("Must catch up missed 9:00 AM run when service was asleep");
        nextRunKarachi.Should().Be(nowKarachi);
    }

    [Fact]
    public void CalculateNextRun_WhenPastScheduledTimeAndAlreadyCompletedToday_ShouldScheduleForTomorrow()
    {
        var karachiTz = JobSchedulerBackgroundService.GetKarachiTimeZone();
        var nowKarachi = new DateTime(2026, 10, 28, 10, 15, 0); // 10:15 AM PKT
        var scheduledTime = new TimeSpan(9, 0, 0); // 9:00 AM PKT
        // Run completed today at 04:00 UTC = 09:00 PKT
        DateTime? lastRunUtc = new DateTime(2026, 10, 28, 4, 0, 0, DateTimeKind.Utc);

        var (nextRunKarachi, isMissedCatchUp) = JobSchedulerBackgroundService.CalculateNextRun(
            nowKarachi, 
            scheduledTime, 
            lastRunUtc, 
            "Completed", 
            karachiTz);

        isMissedCatchUp.Should().BeFalse();
        nextRunKarachi.Should().Be(new DateTime(2026, 10, 29, 9, 0, 0), "Must schedule for tomorrow once today's run has completed");
    }

    [Fact]
    public void SanitizeError_ShouldScrubPasswordsAndTokens()
    {
        var raw1 = "Authentication failed with password=superSecretPassword123 at endpoint";
        var clean1 = AgentOrchestrator.SanitizeError(raw1);
        clean1.Should().NotContain("superSecretPassword123");
        clean1.Should().Contain("***REDACTED***");

        var raw2 = "Authorization failed for Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.xyz";
        var clean2 = AgentOrchestrator.SanitizeError(raw2);
        clean2.Should().NotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.xyz");
        clean2.Should().Contain("Bearer ***REDACTED***");
    }
}
