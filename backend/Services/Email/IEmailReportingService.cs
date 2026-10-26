using JYCareerAgent.Api.Models;

namespace JYCareerAgent.Api.Services.Email;

public interface IEmailReportingService
{
    Task<EmailReportRecord> GenerateDailyReportRecordAsync(
        DateTime runStartTimeUtc,
        DateTime runEndTimeUtc,
        List<SourceRunSummary> sourcesAttempted,
        List<JobListing> allDiscoveredJobs,
        List<JobListing> uniqueJobs,
        List<JobApplication> applicationsProcessed,
        int duplicatesSkipped,
        CancellationToken cancellationToken = default);

    Task<bool> SendDailyReportAsync(EmailReportRecord report, CancellationToken cancellationToken = default);
    Task<(bool success, string message)> SendTestEmailAsync(string? overrideRecipient = null, CancellationToken cancellationToken = default);
    Task<List<EmailReportRecord>> GetReportHistoryAsync(CancellationToken cancellationToken = default);
}
