namespace JYCareerAgent.Api.Models;

public class AgentState
{
    public string Status { get; set; } = "Idle"; // Idle, Running, Interrupted, Completed, Failed
    public DateTime? LastRunStartTime { get; set; }
    public DateTime? LastRunEndTime { get; set; }
    public string LastRunStatus { get; set; } = "NeverRun";
    public DateTime? NextScheduledRun { get; set; }
    public int CurrentJobIndex { get; set; } = 0;
    public int TotalJobsFoundToday { get; set; } = 0;
    public int UniqueJobsFoundToday { get; set; } = 0;
    public int MatchedJobsToday { get; set; } = 0;
    public int ApplicationsSubmittedToday { get; set; } = 0;
    public int ApplicationsPendingToday { get; set; } = 0;
    public int ApplicationsFailedToday { get; set; } = 0;
    public string? LastCorrelationId { get; set; }
    public string? LastError { get; set; }
}
