using JYCareerAgent.Api.Models;

namespace JYCareerAgent.Api.Services.Applications;

public interface IApplicationWorkflowService
{
    Task<JobApplication> PrepareApplicationAsync(JobListing job, CandidateProfile profile, AgentSettings settings, CancellationToken cancellationToken = default);
    Task<JobApplication> SubmitApplicationAsync(string applicationId, bool userExplicitApproval = false, CancellationToken cancellationToken = default);
    Task<List<JobApplication>> GetApplicationsAsync(CancellationToken cancellationToken = default);
    Task<JobApplication?> GetApplicationByIdAsync(string applicationId, CancellationToken cancellationToken = default);
    Task<JobApplication> UpdateApplicationStatusAsync(string applicationId, ApplicationStatus newStatus, string reason, string performedBy = "User", CancellationToken cancellationToken = default);
    Task<JobApplication> UpdateApplicationContentAsync(string applicationId, string coverLetterText, CancellationToken cancellationToken = default);
    Task<int> GetTodayConfirmedApplicationCountAsync(CancellationToken cancellationToken = default);
}
