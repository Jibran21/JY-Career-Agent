using System.Text.Json.Serialization;

namespace JYCareerAgent.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ApplicationStatus
{
    Discovered,
    Matched,
    Skipped,
    PendingApproval,
    ReadyToSubmit,
    InProgress,
    Submitted,
    SubmissionUnconfirmed,
    Failed,
    Interview,
    Rejected,
    Offer,
    Withdrawn
}

public class JobApplication
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string JobId { get; set; } = string.Empty;
    public string Employer { get; set; } = string.Empty;
    public string PositionTitle { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string JobUrl { get; set; } = string.Empty;
    public double MatchScore { get; set; }
    public string CvVersionUsed { get; set; } = string.Empty;
    public string CvFilePath { get; set; } = string.Empty;
    public string CoverLetterVersionUsed { get; set; } = string.Empty;
    public string CoverLetterFilePath { get; set; } = string.Empty;
    public string GeneratedCoverLetterText { get; set; } = string.Empty;
    public OperatingMode SubmissionMode { get; set; } = OperatingMode.ApprovalRequired;
    public ApplicationStatus Status { get; set; } = ApplicationStatus.PendingApproval;
    public int AttemptCount { get; set; } = 0;
    public DateTime ApplicationStartTimeUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SubmissionTimestampUtc { get; set; }
    public string TimeZone { get; set; } = "Asia/Karachi";
    public string? ConfirmationReference { get; set; }
    public string? ConfirmationUrl { get; set; }
    public string? ErrorDetails { get; set; }
    public string? PendingReason { get; set; }
    public string NextRecommendedAction { get; set; } = "Review prepared CV and cover letter in dashboard.";
    public List<ScreeningQuestionAnswer> ScreeningQuestions { get; set; } = new();
    public List<AuditEntry> AuditHistory { get; set; } = new();

    // Helper for formatting Asia/Karachi local time
    public string GetSubmissionTimeKarachi()
    {
        if (!SubmissionTimestampUtc.HasValue) return "Not submitted";
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi");
            var local = TimeZoneInfo.ConvertTimeFromUtc(SubmissionTimestampUtc.Value, tz);
            return local.ToString("yyyy-MM-dd HH:mm:ss 'PKT'");
        }
        catch
        {
            return SubmissionTimestampUtc.Value.AddHours(5).ToString("yyyy-MM-dd HH:mm:ss 'PKT'");
        }
    }
}

public class ScreeningQuestionAnswer
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; } = false;
    public string Reason { get; set; } = string.Empty;
}

public class AuditEntry
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string PerformedBy { get; set; } = "System";
    public string Details { get; set; } = string.Empty;
    public ApplicationStatus PreviousStatus { get; set; }
    public ApplicationStatus NewStatus { get; set; }
}
