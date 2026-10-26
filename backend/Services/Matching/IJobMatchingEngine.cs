using JYCareerAgent.Api.Models;

namespace JYCareerAgent.Api.Services.Matching;

public interface IJobMatchingEngine
{
    MatchScoreResult EvaluateJob(JobListing job, CandidateProfile profile, AgentSettings settings);
}
