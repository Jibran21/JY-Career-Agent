namespace JYCareerAgent.Api.Models;

public class EmailReportRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ReportDate { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");
    public DateTime RunStartTimeUtc { get; set; }
    public DateTime RunEndTimeUtc { get; set; }
    public string DurationFormatted { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public bool DeliverySuccess { get; set; } = false;
    public string DeliveryStatusMessage { get; set; } = string.Empty;
    public string? ProviderMessageId { get; set; }
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;

    // Metrics
    public List<SourceRunSummary> SourcesAttempted { get; set; } = new();
    public int TotalJobsDiscovered { get; set; }
    public int TotalUniqueJobs { get; set; }
    public int HighMatchJobsCount { get; set; }
    public int ApplicationsSubmittedCount { get; set; }
    public int ApplicationsPendingCount { get; set; }
    public int ApplicationsSkippedCount { get; set; }
    public int DuplicatesSkippedCount { get; set; }
    public int FailedSubmissionsCount { get; set; }
    public int SubmissionUnconfirmedCount { get; set; }

    // Categorized applications
    public List<ApplicationReportSummary> ConfirmedSubmissions { get; set; } = new();
    public List<ApplicationReportSummary> PendingApprovals { get; set; } = new();
    public List<ApplicationReportSummary> FailedAttempts { get; set; } = new();
    public List<ApplicationReportSummary> SubmissionUnconfirmedCases { get; set; } = new();
    public List<ApplicationReportSummary> SkippedOpportunities { get; set; } = new();

    public string HtmlBody { get; set; } = string.Empty;
    public string PlainTextBody { get; set; } = string.Empty;
}

public class SourceRunSummary
{
    public string SourceName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int JobsFound { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ApplicationReportSummary
{
    public string CompanyName { get; set; } = string.Empty;
    public string PositionTitle { get; set; } = string.Empty;
    public string SourcePlatform { get; set; } = string.Empty;
    public string JobLocation { get; set; } = string.Empty;
    public string JobUrl { get; set; } = string.Empty;
    public double MatchScore { get; set; }
    public string ApplicationStatus { get; set; } = string.Empty;
    public string SubmissionTimeKarachi { get; set; } = string.Empty;
    public string? ConfirmationReference { get; set; }
    public string CvVersionUsed { get; set; } = string.Empty;
    public string? ErrorOrPendingReason { get; set; }
    public string RecommendedNextAction { get; set; } = string.Empty;
}
