using FluentAssertions;
using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Matching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JYCareerAgent.Tests;

public class JobMatchingTests
{
    private readonly JobMatchingEngine _matchingEngine = new(NullLogger<JobMatchingEngine>.Instance);

    private static CandidateProfile CreateTestProfile()
    {
        return new CandidateProfile
        {
            FullName = "Jibran Yousuf Khan",
            Headline = "Data & Software Engineer | .NET • Angular • Data • AI",
            Location = "Karachi, Pakistan",
            Skills = new SkillCatalog
            {
                LanguagesAndFrameworks = new List<string> { "C#", ".NET Core", "ASP.NET Core", "Angular", "TypeScript", "ASP.NET MVC" },
                DatabaseAndReporting = new List<string> { "MS SQL Server", "SSIS", "Power BI", "Entity Framework" },
                ArchitectureAndMessaging = new List<string> { "Clean Architecture", "SignalR", "RESTful APIs" },
                AiAndAgentic = new List<string> { "Microsoft Semantic Kernel", "AI agents", "LLM API integration" }
            }
        };
    }

    [Fact]
    public void EvaluateJob_HighMatchSeniorDotNetAngularRole_ShouldScoreAbove80()
    {
        // Arrange
        var profile = CreateTestProfile();
        var settings = new AgentSettings();

        var job = new JobListing
        {
            Title = "Senior .NET Core & Angular Full Stack Engineer",
            Employer = "FinTech Global",
            Location = "Karachi, Pakistan",
            WorkArrangement = WorkArrangement.Hybrid,
            Description = "We are seeking a Senior .NET and Angular engineer with 8-10 years experience. Deep knowledge of C#, ASP.NET Core Web API, Entity Framework, Clean Architecture, Angular, TypeScript, and MS SQL Server query optimization. Experience in FinTech or banking is a plus.",
            RequiredSkills = new List<string> { "C#", ".NET Core", "Angular", "TypeScript", "SQL Server", "Clean Architecture" }
        };

        // Act
        var result = _matchingEngine.EvaluateJob(job, profile, settings);

        // Assert
        result.TotalScore.Should().BeGreaterThanOrEqualTo(80.0);
        result.Recommendation.Should().Be("HighPriority");
        result.MatchingSkills.Should().Contain(new[] { "C#", ".NET Core", "Angular", "TypeScript" });
        result.ReasonsToApply.Should().NotBeEmpty();
    }

    [Fact]
    public void EvaluateJob_UnrelatedJuniorRole_ShouldScoreBelow65()
    {
        // Arrange
        var profile = CreateTestProfile();
        var settings = new AgentSettings();

        var job = new JobListing
        {
            Title = "Junior Python / Django Developer Intern",
            Employer = "Startup Labs",
            Location = "Berlin, Germany (On-Site)",
            WorkArrangement = WorkArrangement.OnSite,
            Description = "Looking for an entry level intern with Python, Django, and PostgreSQL experience. Must be located on-site in Berlin with valid EU work authorization.",
            RequiredSkills = new List<string> { "Python", "Django", "PostgreSQL" }
        };

        // Act
        var result = _matchingEngine.EvaluateJob(job, profile, settings);

        // Assert
        result.TotalScore.Should().BeLessThan(65.0);
        result.Recommendation.Should().Be("Skip");
        result.PotentialConcerns.Should().NotBeEmpty();
    }

    [Fact]
    public void EvaluateJob_ConfigurableWeights_ShouldInfluenceFinalScore()
    {
        // Arrange
        var profile = CreateTestProfile();
        var settingsDefault = new AgentSettings();

        var settingsHeavySql = new AgentSettings
        {
            MatchWeights = new MatchWeights
            {
                DotNetWeight = 5,
                AngularWeight = 5,
                SeniorityWeight = 10,
                SqlWeight = 70, // Emphasize SQL heavily
                IndustryWeight = 5,
                LocationWeight = 5
            }
        };

        var sqlJob = new JobListing
        {
            Title = "Lead Data & ETL Specialist",
            Employer = "Enterprise Data Corp",
            Location = "Karachi, Pakistan",
            Description = "MS SQL Server, SSIS, ETL pipelines, Power BI, T-SQL performance tuning.",
            RequiredSkills = new List<string> { "MS SQL Server", "SSIS", "ETL", "Power BI" }
        };

        // Act
        var defaultResult = _matchingEngine.EvaluateJob(sqlJob, profile, settingsDefault);
        var heavySqlResult = _matchingEngine.EvaluateJob(sqlJob, profile, settingsHeavySql);

        // Assert
        heavySqlResult.TotalScore.Should().BeGreaterThan(defaultResult.TotalScore);
    }
}
