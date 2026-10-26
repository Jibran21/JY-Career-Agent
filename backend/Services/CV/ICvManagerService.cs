using JYCareerAgent.Api.Models;

namespace JYCareerAgent.Api.Services.CV;

public interface ICvManagerService
{
    Task<string> GetMasterCvTextAsync(CancellationToken cancellationToken = default);
    Task<CandidateProfile> GetProfileAsync(CancellationToken cancellationToken = default);
    Task UpdateProfileAsync(CandidateProfile profile, CancellationToken cancellationToken = default);
    Task<(string fileName, string fullPath, string content)> GenerateTailoredCvAsync(JobListing job, CandidateProfile profile, CancellationToken cancellationToken = default);
    Task<List<TailoredCvMetadata>> ListTailoredCvsAsync(CancellationToken cancellationToken = default);
    Task<string?> GetTailoredCvContentAsync(string fileName, CancellationToken cancellationToken = default);
    Task<string> GetTailoredCvPathAsync(string fileName);
}

public class TailoredCvMetadata
{
    public string FileName { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string Employer { get; set; } = string.Empty;
    public string PositionTitle { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public long FileSizeBytes { get; set; }
}
