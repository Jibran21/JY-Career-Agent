using JYCareerAgent.Api.Models;

namespace JYCareerAgent.Api.Services.AI;

public interface IAiAgentService
{
    Task<string> GenerateCoverLetterAsync(JobListing job, CandidateProfile profile, AgentSettings settings, CancellationToken cancellationToken = default);
    Task<List<ScreeningQuestionAnswer>> EvaluateScreeningQuestionsAsync(List<string> questions, CandidateProfile profile, CancellationToken cancellationToken = default);
    Task<string> TailorCvContentAsync(JobListing job, CandidateProfile profile, string masterCvText, AgentSettings settings, CancellationToken cancellationToken = default);
}
