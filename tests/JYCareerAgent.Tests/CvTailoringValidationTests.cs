using FluentAssertions;
using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.CV;
using JYCareerAgent.Api.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JYCareerAgent.Tests;

public class CvTailoringValidationTests : IDisposable
{
    private readonly string _testDir;
    private readonly CvManagerService _cvManagerService;

    public CvTailoringValidationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "jy_cv_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "JY_STORAGE_ROOT", _testDir }
            })
            .Build();

        var storage = new JsonStorageService(config, NullLogger<JsonStorageService>.Instance);
        _cvManagerService = new CvManagerService(storage, config, NullLogger<CvManagerService>.Instance);
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
    public async Task GenerateTailoredCvAsync_ShouldPreserveExactVerifiedEmployersAndDegrees()
    {
        // Arrange
        var profile = new CandidateProfile
        {
            FullName = "Jibran Yousuf Khan",
            Headline = "Data & Software Engineer | .NET • Angular • Data • AI",
            Location = "Karachi, Pakistan",
            Email = "jibrankhalily@gmail.com",
            Phone = "+92 321 2351118",
            Experience = new List<WorkExperience>
            {
                new() { Company = "Think Enabled", Title = "Senior Consultant / Senior Software Engineer", Period = "Jan 2024 – Present", Location = "Karachi, Pakistan" },
                new() { Company = "Aion Digital", Title = "Software Engineer", Period = "Mar 2021 – Dec 2023", Location = "Karachi, Pakistan" },
                new() { Company = "Out2Sol", Title = "Team Lead", Period = "Oct 2019 – Feb 2021", Location = "Karachi, Pakistan" },
                new() { Company = "Smart Works Systems", Title = "ERP Developer", Period = "Sep 2015 – Oct 2019", Location = "Karachi, Pakistan" }
            },
            Education = new List<EducationEntry>
            {
                new() { Degree = "Master of Science in Computer Science (MSCS)", Institution = "Virtual University, Islamabad", Year = "2017" }
            }
        };

        var job = new JobListing
        {
            Id = "job-401",
            Employer = "Systems Limited",
            Title = "Senior .NET Core Architect",
            Location = "Karachi"
        };

        // Act
        var (fileName, fullPath, content) = await _cvManagerService.GenerateTailoredCvAsync(job, profile);

        // Assert
        File.Exists(fullPath).Should().BeTrue();
        fileName.Should().Contain("SystemsLimited");

        // Verify Ground Truth Integrity: All real employers must be present
        content.Should().Contain("Think Enabled");
        content.Should().Contain("Aion Digital");
        content.Should().Contain("Out2Sol");
        content.Should().Contain("Smart Works Systems");
        content.Should().Contain("Virtual University, Islamabad");

        // Verify no false claims or made-up degrees
        content.Should().NotContain("PhD");
        content.Should().NotContain("Google Inc");
    }
}
