using System.Text.Json.Serialization;

namespace JYCareerAgent.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkArrangement
{
    Remote,
    Hybrid,
    OnSite,
    Unspecified
}

public class JobListing
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PlatformJobId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Employer { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> RequiredSkills { get; set; } = new();
    public string ExperienceRequirements { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public WorkArrangement WorkArrangement { get; set; } = WorkArrangement.Unspecified;
    public string SalaryRange { get; set; } = string.Empty;
    public DateTime? PostedDate { get; set; }
    public DateTime? ApplicationDeadline { get; set; }
    public string SourcePlatform { get; set; } = string.Empty;
    public string JobUrl { get; set; } = string.Empty;
    public string CanonicalUrl { get; set; } = string.Empty;
    public string EmployerCareerPageUrl { get; set; } = string.Empty;
    public string ApplicationMethod { get; set; } = "Direct"; // Direct, Email, EasyApply, Portal
    public bool IsOpen { get; set; } = true;
    public DateTime DiscoveredAtUtc { get; set; } = DateTime.UtcNow;

    // Evaluated matching results
    public MatchScoreResult? MatchScore { get; set; }
    public bool IsReviewed { get; set; } = false;
    public bool IsSkipped { get; set; } = false;
    public string? SkipReason { get; set; }
}

public class MatchScoreResult
{
    public double TotalScore { get; set; }
    public List<string> MatchingSkills { get; set; } = new();
    public List<string> MissingSkills { get; set; } = new();
    public string SeniorityFit { get; set; } = string.Empty;
    public string LocationFit { get; set; } = string.Empty;
    public List<string> ReasonsToApply { get; set; } = new();
    public List<string> PotentialConcerns { get; set; } = new();
    public string Recommendation { get; set; } = "Skip"; // HighPriority, ApprovalQueue, Skip
    public string Summary { get; set; } = string.Empty;

    // Breakdown components
    public double DotNetScore { get; set; }
    public double AngularScore { get; set; }
    public double SeniorityScore { get; set; }
    public double SqlScore { get; set; }
    public double IndustryScore { get; set; }
    public double LocationScore { get; set; }
}
