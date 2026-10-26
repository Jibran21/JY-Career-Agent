using JYCareerAgent.Api.Models;

namespace JYCareerAgent.Api.Services.JobSources;

public class JobSourceResult
{
    public string SourceName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public List<JobListing> Jobs { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string AccessType { get; set; } = "Api"; // Api, Feed, ApprovalAssisted
    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
}

public interface IJobSource
{
    string SourceName { get; }
    string AccessType { get; }
    Task<JobSourceResult> SearchJobsAsync(AgentSettings settings, CancellationToken cancellationToken = default);
}
