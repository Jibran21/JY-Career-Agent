using FluentAssertions;
using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Matching;
using Xunit;

namespace JYCareerAgent.Tests;

public class JobDeduplicationTests
{
    private readonly JobNormalizationService _normalizationService = new();

    [Theory]
    [InlineData("https://linkedin.com/jobs/view/12345?utm_source=feed&ref=xyz", "https://linkedin.com/jobs/view/12345")]
    [InlineData("https://careers.company.com/job/senior-net?source=google&gh_src=123", "https://careers.company.com/job/senior-net")]
    [InlineData("https://rozee.pk/job/101?trackingid=abc", "https://rozee.pk/job/101")]
    public void CanonicalizeUrl_ShouldRemoveTrackingParameters(string rawUrl, string expectedCleanUrl)
    {
        var clean = _normalizationService.CanonicalizeUrl(rawUrl);
        clean.Should().Be(expectedCleanUrl);
    }

    [Fact]
    public void IsDuplicate_ShouldDetectExactPlatformIdMatch()
    {
        var existing = new List<JobListing>
        {
            new() { SourcePlatform = "Remotive", PlatformJobId = "rem-101", Title = "Backend Dev" }
        };

        var candidate = new JobListing { SourcePlatform = "Remotive", PlatformJobId = "rem-101", Title = "Senior Backend Dev" };

        var isDup = _normalizationService.IsDuplicate(candidate, existing);
        isDup.Should().BeTrue();
    }

    [Fact]
    public void IsDuplicate_ShouldDetectCanonicalUrlMatchEvenIfTrackingParamsDiffer()
    {
        var existing = new List<JobListing>
        {
            new() { JobUrl = "https://example.com/jobs/99", CanonicalUrl = "https://example.com/jobs/99" }
        };

        var candidate = new JobListing
        {
            JobUrl = "https://example.com/jobs/99?utm_source=newsletter&utm_medium=email"
        };

        var isDup = _normalizationService.IsDuplicate(candidate, existing);
        isDup.Should().BeTrue();
    }

    [Fact]
    public void IsDuplicate_ShouldDetectFingerprintMatch_WhenEmployerTitleLocationMatch()
    {
        var existing = new List<JobListing>
        {
            new()
            {
                Employer = "Systems Limited",
                Title = "Senior .NET Core Developer",
                Location = "Karachi, Pakistan"
            }
        };

        var candidate = new JobListing
        {
            Employer = "Systems Limited ",
            Title = "Senior .NET Core Developer",
            Location = "Karachi, Pakistan"
        };

        var isDup = _normalizationService.IsDuplicate(candidate, existing);
        isDup.Should().BeTrue();
    }

    [Fact]
    public void IsDuplicate_ShouldReturnFalse_ForDistinctJobs()
    {
        var existing = new List<JobListing>
        {
            new() { PlatformJobId = "job-1", Employer = "Think Enabled", Title = "Senior .NET Architect" }
        };

        var candidate = new JobListing
        {
            PlatformJobId = "job-2",
            Employer = "Aion Digital",
            Title = "Senior Angular Developer"
        };

        var isDup = _normalizationService.IsDuplicate(candidate, existing);
        isDup.Should().BeFalse();
    }
}
