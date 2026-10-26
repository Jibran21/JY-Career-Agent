using JYCareerAgent.Api.Models;

namespace JYCareerAgent.Api.Services.Matching;

public interface IJobNormalizationService
{
    string CanonicalizeUrl(string rawUrl);
    string CreateJobFingerprint(string employer, string title, string location);
    bool IsDuplicate(JobListing candidate, IEnumerable<JobListing> existingJobs);
    void NormalizeJob(JobListing job);
}
