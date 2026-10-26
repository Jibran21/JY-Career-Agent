using System.Text;
using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Storage;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace JYCareerAgent.Api.Services.Email;

public class EmailReportingService : IEmailReportingService
{
    private readonly IJsonStorageService _storageService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailReportingService> _logger;

    public EmailReportingService(
        IJsonStorageService storageService,
        IConfiguration configuration,
        ILogger<EmailReportingService> logger)
    {
        _storageService = storageService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<EmailReportRecord>> GetReportHistoryAsync(CancellationToken cancellationToken = default)
    {
        var list = await _storageService.LoadAsync<List<EmailReportRecord>>("data/email-reports.json", cancellationToken);
        return list ?? new List<EmailReportRecord>();
    }

    public Task<EmailReportRecord> GenerateDailyReportRecordAsync(
        DateTime runStartTimeUtc,
        DateTime runEndTimeUtc,
        List<SourceRunSummary> sourcesAttempted,
        List<JobListing> allDiscoveredJobs,
        List<JobListing> uniqueJobs,
        List<JobApplication> applicationsProcessed,
        int duplicatesSkipped,
        CancellationToken cancellationToken = default)
    {
        var duration = runEndTimeUtc - runStartTimeUtc;
        var durationFormatted = $"{(int)duration.TotalMinutes}m {duration.Seconds}s";

        var confirmed = applicationsProcessed.Where(a => a.Status == ApplicationStatus.Submitted).ToList();
        var pending = applicationsProcessed.Where(a => a.Status == ApplicationStatus.PendingApproval || a.Status == ApplicationStatus.ReadyToSubmit).ToList();
        var failed = applicationsProcessed.Where(a => a.Status == ApplicationStatus.Failed).ToList();
        var unconfirmed = applicationsProcessed.Where(a => a.Status == ApplicationStatus.SubmissionUnconfirmed).ToList();
        var skipped = applicationsProcessed.Where(a => a.Status == ApplicationStatus.Skipped).ToList();

        var dateStr = runStartTimeUtc.ToString("yyyy-MM-dd");
        var subject = $"JY Career Agent Daily Report — {dateStr} — {confirmed.Count} Applications Submitted";

        var record = new EmailReportRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            ReportDate = dateStr,
            RunStartTimeUtc = runStartTimeUtc,
            RunEndTimeUtc = runEndTimeUtc,
            DurationFormatted = durationFormatted,
            Subject = subject,
            SourcesAttempted = sourcesAttempted,
            TotalJobsDiscovered = allDiscoveredJobs.Count,
            TotalUniqueJobs = uniqueJobs.Count,
            HighMatchJobsCount = uniqueJobs.Count(j => j.MatchScore?.TotalScore >= 80),
            ApplicationsSubmittedCount = confirmed.Count,
            ApplicationsPendingCount = pending.Count,
            ApplicationsSkippedCount = skipped.Count,
            DuplicatesSkippedCount = duplicatesSkipped,
            FailedSubmissionsCount = failed.Count,
            SubmissionUnconfirmedCount = unconfirmed.Count,

            ConfirmedSubmissions = confirmed.Select(MapSummary).ToList(),
            PendingApprovals = pending.Select(MapSummary).ToList(),
            FailedAttempts = failed.Select(MapSummary).ToList(),
            SubmissionUnconfirmedCases = unconfirmed.Select(MapSummary).ToList(),
            SkippedOpportunities = skipped.Select(MapSummary).ToList()
        };

        record.HtmlBody = BuildHtmlReport(record);
        record.PlainTextBody = BuildPlainTextReport(record);

        return Task.FromResult(record);
    }

    public async Task<bool> SendDailyReportAsync(EmailReportRecord report, CancellationToken cancellationToken = default)
    {
        var settings = await _storageService.LoadAsync<AgentSettings>("data/settings.json", cancellationToken) ?? new AgentSettings();
        report.RecipientEmail = settings.Email.RecipientEmail;

        if (!settings.Email.DailyReportEnabled)
        {
            _logger.LogInformation("Daily email reporting is disabled in settings.");
            report.DeliverySuccess = false;
            report.DeliveryStatusMessage = "Email delivery skipped (DailyReportEnabled = false in settings).";
            await SaveReportRecordAsync(report, cancellationToken);
            return false;
        }

        var (success, message, messageId) = await SendEmailInternalAsync(
            settings.Email, 
            report.RecipientEmail, 
            report.Subject, 
            report.HtmlBody, 
            report.PlainTextBody, 
            cancellationToken);

        report.DeliverySuccess = success;
        report.DeliveryStatusMessage = message;
        report.ProviderMessageId = messageId;
        report.SentAtUtc = DateTime.UtcNow;

        await SaveReportRecordAsync(report, cancellationToken);
        return success;
    }

    public async Task<(bool success, string message)> SendTestEmailAsync(string? overrideRecipient = null, CancellationToken cancellationToken = default)
    {
        var settings = await _storageService.LoadAsync<AgentSettings>("data/settings.json", cancellationToken) ?? new AgentSettings();
        var recipient = !string.IsNullOrWhiteSpace(overrideRecipient) ? overrideRecipient : settings.Email.RecipientEmail;

        var subject = $"JY Career Agent Test Email — {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";
        var body = $@"
        <html>
        <body style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
            <h2 style='color: #2563eb;'>JY Career Agent — Test Notification</h2>
            <p>This is a test email sent from <strong>JY Career Agent</strong>.</p>
            <p>Timezone: <strong>Asia/Karachi</strong></p>
            <p>Current Time (Karachi): <strong>{FormatKarachiTime(DateTime.UtcNow)}</strong></p>
            <p>If you received this message, your SMTP connection settings are working properly.</p>
        </body>
        </html>";

        var (success, message, _) = await SendEmailInternalAsync(
            settings.Email, 
            recipient, 
            subject, 
            body, 
            "JY Career Agent Test Email - SMTP configuration verified.", 
            cancellationToken);

        return (success, message);
    }

    private async Task<(bool success, string message, string? messageId)> SendEmailInternalAsync(
        EmailSettings emailSettings,
        string recipient,
        string subject,
        string htmlBody,
        string plainText,
        CancellationToken cancellationToken)
    {
        // Read credentials safely from environment variables (never from JSON or hardcoded)
        var smtpPassword = Environment.GetEnvironmentVariable("JY_EMAIL__SMTP_PASSWORD") ?? 
                           _configuration["JY_EMAIL:SMTP_PASSWORD"] ?? 
                           _configuration["JY_EMAIL__SMTP_PASSWORD"] ?? 
                           _configuration["Email:SmtpPassword"];
        var smtpUsername = Environment.GetEnvironmentVariable("JY_EMAIL__SMTP_USERNAME") ?? 
                           _configuration["JY_EMAIL:SMTP_USERNAME"] ?? 
                           _configuration["JY_EMAIL__SMTP_USERNAME"] ?? 
                           _configuration["Email:SmtpUsername"] ?? 
                           emailSettings.SenderEmail;

        if (string.IsNullOrWhiteSpace(smtpPassword))
        {
            var notice = "SMTP Password not configured in environment variable 'JY_EMAIL__SMTP_PASSWORD'. Report generated and logged locally.";
            _logger.LogWarning(notice);
            return (false, notice, null);
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("JY Career Agent", emailSettings.SenderEmail));
            message.To.Add(new MailboxAddress("Jibran Yousuf Khan", recipient));
            message.Subject = subject;

            var builder = new BodyBuilder
            {
                TextBody = plainText,
                HtmlBody = htmlBody
            };
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            // Connect with timeout
            client.Timeout = 15000;
            var secureOption = emailSettings.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(emailSettings.SmtpHost, emailSettings.SmtpPort, secureOption, cancellationToken);
            
            if (!string.IsNullOrWhiteSpace(smtpUsername))
            {
                await client.AuthenticateAsync(smtpUsername, smtpPassword, cancellationToken);
            }

            var response = await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("Email sent successfully to {Recipient}. Response: {Response}", recipient, response);
            return (true, $"Delivered successfully via {emailSettings.SmtpHost}. Response: {response}", message.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Recipient} via {Host}", recipient, emailSettings.SmtpHost);
            return (false, $"Email delivery error: {ex.Message}", null);
        }
    }

    private async Task SaveReportRecordAsync(EmailReportRecord report, CancellationToken cancellationToken)
    {
        var reports = await GetReportHistoryAsync(cancellationToken);
        reports.Insert(0, report);
        // Keep latest 100 daily reports
        if (reports.Count > 100) reports = reports.Take(100).ToList();
        await _storageService.SaveAsync("data/email-reports.json", reports, createBackup: false, cancellationToken);
    }

    private static ApplicationReportSummary MapSummary(JobApplication app)
    {
        return new ApplicationReportSummary
        {
            CompanyName = app.Employer,
            PositionTitle = app.PositionTitle,
            SourcePlatform = app.Platform,
            JobLocation = app.TimeZone,
            JobUrl = app.JobUrl,
            MatchScore = app.MatchScore,
            ApplicationStatus = app.Status.ToString(),
            SubmissionTimeKarachi = app.GetSubmissionTimeKarachi(),
            ConfirmationReference = app.ConfirmationReference,
            CvVersionUsed = app.CvVersionUsed,
            ErrorOrPendingReason = app.ErrorDetails ?? app.PendingReason,
            RecommendedNextAction = app.NextRecommendedAction
        };
    }

    private string BuildHtmlReport(EmailReportRecord report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><style>");
        sb.AppendLine("body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; color: #1e293b; line-height: 1.5; margin: 0; padding: 20px; background-color: #f8fafc; }");
        sb.AppendLine(".container { max-width: 800px; margin: 0 auto; background: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.1); }");
        sb.AppendLine(".header { background: linear-gradient(135deg, #1e3a8a 0%, #0284c7 100%); color: #ffffff; padding: 28px; }");
        sb.AppendLine(".stats-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; padding: 20px; background: #f1f5f9; border-bottom: 1px solid #e2e8f0; }");
        sb.AppendLine(".stat-card { background: #ffffff; padding: 14px; border-radius: 8px; text-align: center; border: 1px solid #e2e8f0; }");
        sb.AppendLine(".stat-num { font-size: 24px; font-weight: bold; color: #0284c7; }");
        sb.AppendLine(".stat-label { font-size: 12px; color: #64748b; text-transform: uppercase; margin-top: 4px; }");
        sb.AppendLine(".content { padding: 24px; }");
        sb.AppendLine(".section-title { font-size: 18px; font-weight: 700; color: #0f172a; margin-top: 24px; margin-bottom: 12px; border-bottom: 2px solid #e2e8f0; padding-bottom: 6px; }");
        sb.AppendLine("table { width: 100%; border-collapse: collapse; margin-bottom: 20px; font-size: 14px; }");
        sb.AppendLine("th { background: #f8fafc; text-align: left; padding: 10px; border-bottom: 2px solid #cbd5e1; color: #475569; }");
        sb.AppendLine("td { padding: 10px; border-bottom: 1px solid #e2e8f0; }");
        sb.AppendLine(".badge { display: inline-block; padding: 3px 8px; border-radius: 9999px; font-size: 12px; font-weight: 600; }");
        sb.AppendLine(".badge-success { background: #dcfce7; color: #15803d; }");
        sb.AppendLine(".badge-pending { background: #fef9c3; color: #854d0e; }");
        sb.AppendLine(".badge-failed { background: #fee2e2; color: #b91c1c; }");
        sb.AppendLine(".footer { padding: 20px; background: #f8fafc; text-align: center; font-size: 13px; color: #64748b; border-top: 1px solid #e2e8f0; }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<div class='container'>");
        sb.AppendLine("<div class='header'>");
        sb.AppendLine("<h1 style='margin:0; font-size: 22px;'>JY Career Agent — Daily Execution Report</h1>");
        sb.AppendLine($"<p style='margin: 6px 0 0 0; opacity: 0.9;'>Candidate: Jibran Yousuf Khan | Run Date: {report.ReportDate} | Duration: {report.DurationFormatted}</p>");
        sb.AppendLine("</div>");

        // Stats grid
        sb.AppendLine("<div class='stats-grid'>");
        sb.AppendLine($"<div class='stat-card'><div class='stat-num'>{report.TotalJobsDiscovered}</div><div class='stat-label'>Discovered</div></div>");
        sb.AppendLine($"<div class='stat-card'><div class='stat-num'>{report.HighMatchJobsCount}</div><div class='stat-label'>High Match (80+)</div></div>");
        sb.AppendLine($"<div class='stat-card'><div class='stat-num' style='color:#16a34a;'>{report.ApplicationsSubmittedCount}</div><div class='stat-label'>Submitted</div></div>");
        sb.AppendLine($"<div class='stat-card'><div class='stat-num' style='color:#ca8a04;'>{report.ApplicationsPendingCount}</div><div class='stat-label'>Pending Approval</div></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='content'>");

        // Sources Status
        sb.AppendLine("<div class='section-title'>Job Sources Status</div>");
        sb.AppendLine("<table><thead><tr><th>Source</th><th>Status</th><th>Jobs Found</th><th>Notes</th></tr></thead><tbody>");
        foreach (var src in report.SourcesAttempted)
        {
            var badge = src.Success ? "<span class='badge badge-success'>Connected</span>" : "<span class='badge badge-failed'>Failed</span>";
            sb.AppendLine($"<tr><td><strong>{src.SourceName}</strong></td><td>{badge}</td><td>{src.JobsFound}</td><td>{src.ErrorMessage ?? "OK"}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");

        // Confirmed Submissions
        sb.AppendLine("<div class='section-title'>Confirmed Submissions</div>");
        if (report.ConfirmedSubmissions.Any())
        {
            sb.AppendLine("<table><thead><tr><th>Employer</th><th>Role</th><th>Platform</th><th>Match</th><th>Submitted (Karachi Time)</th><th>Reference</th></tr></thead><tbody>");
            foreach (var app in report.ConfirmedSubmissions)
            {
                sb.AppendLine($"<tr><td><strong>{app.CompanyName}</strong></td><td>{app.PositionTitle}</td><td>{app.SourcePlatform}</td><td><strong>{app.MatchScore:F0}%</strong></td><td>{app.SubmissionTimeKarachi}</td><td><code>{app.ConfirmationReference}</code></td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }
        else
        {
            sb.AppendLine("<p style='color: #64748b; font-style: italic;'>No automated submissions were finalized during this run (Operating mode is Approval Required).</p>");
        }

        // Pending Approval
        sb.AppendLine("<div class='section-title'>Applications Pending Approval</div>");
        if (report.PendingApprovals.Any())
        {
            sb.AppendLine("<table><thead><tr><th>Employer</th><th>Role</th><th>Score</th><th>Reason / Required Action</th><th>Job Link</th></tr></thead><tbody>");
            foreach (var app in report.PendingApprovals)
            {
                sb.AppendLine($"<tr><td><strong>{app.CompanyName}</strong></td><td>{app.PositionTitle}</td><td>{app.MatchScore:F0}%</td><td>{app.ErrorOrPendingReason ?? "Awaiting candidate approval"}</td><td><a href='{app.JobUrl}' target='_blank'>View Job</a></td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }
        else
        {
            sb.AppendLine("<p style='color: #64748b; font-style: italic;'>No applications currently pending approval.</p>");
        }

        // Failed or Unconfirmed
        if (report.FailedAttempts.Any() || report.SubmissionUnconfirmedCases.Any())
        {
            sb.AppendLine("<div class='section-title'>Failed or Unconfirmed Attempts</div>");
            sb.AppendLine("<table><thead><tr><th>Employer</th><th>Role</th><th>Status</th><th>Reason</th></tr></thead><tbody>");
            foreach (var item in report.FailedAttempts.Concat(report.SubmissionUnconfirmedCases))
            {
                sb.AppendLine($"<tr><td><strong>{item.CompanyName}</strong></td><td>{item.PositionTitle}</td><td><span class='badge badge-failed'>{item.ApplicationStatus}</span></td><td>{item.ErrorOrPendingReason}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        sb.AppendLine("</div>"); // End content

        // Footer
        sb.AppendLine("<div class='footer'>");
        sb.AppendLine("<p>To review, edit documents, or approve applications, open your local dashboard:</p>");
        sb.AppendLine("<p><strong>Frontend Dashboard:</strong> <a href='http://localhost:4200'>http://localhost:4200</a> | <strong>Backend API:</strong> <a href='http://localhost:5000/swagger'>http://localhost:5000/swagger</a></p>");
        sb.AppendLine("<p>JY Career Agent • Strictly Confidential • Built for Jibran Yousuf Khan</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    private string BuildPlainTextReport(EmailReportRecord report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"================================================================================");
        sb.AppendLine($"JY CAREER AGENT DAILY REPORT — {report.ReportDate}");
        sb.AppendLine($"================================================================================");
        sb.AppendLine($"Candidate: Jibran Yousuf Khan");
        sb.AppendLine($"Duration:  {report.DurationFormatted}");
        sb.AppendLine($"Jobs Discovered:     {report.TotalJobsDiscovered}");
        sb.AppendLine($"Unique Jobs:         {report.TotalUniqueJobs}");
        sb.AppendLine($"High Matches (80+):  {report.HighMatchJobsCount}");
        sb.AppendLine($"Confirmed Submitted: {report.ApplicationsSubmittedCount}");
        sb.AppendLine($"Pending Approval:    {report.ApplicationsPendingCount}");
        sb.AppendLine($"Skipped:             {report.ApplicationsSkippedCount}");
        sb.AppendLine($"Duplicates Skipped:  {report.DuplicatesSkippedCount}");
        sb.AppendLine();
        sb.AppendLine("CONFIRMED SUBMISSIONS:");
        if (report.ConfirmedSubmissions.Any())
        {
            foreach (var c in report.ConfirmedSubmissions)
            {
                sb.AppendLine($"• {c.CompanyName} - {c.PositionTitle} (Match: {c.MatchScore:F0}%) | Time: {c.SubmissionTimeKarachi} | Ref: {c.ConfirmationReference}");
            }
        }
        else
        {
            sb.AppendLine("  (None submitted automatically - awaiting explicit approval in dashboard)");
        }
        sb.AppendLine();
        sb.AppendLine("PENDING APPROVAL:");
        foreach (var p in report.PendingApprovals)
        {
            sb.AppendLine($"• {p.CompanyName} - {p.PositionTitle} (Match: {p.MatchScore:F0}%) - Reason: {p.ErrorOrPendingReason} - Link: {p.JobUrl}");
        }
        sb.AppendLine();
        sb.AppendLine("Open local dashboard to review: http://localhost:4200");
        return sb.ToString();
    }

    private static string FormatKarachiTime(DateTime utcTime)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi");
            return TimeZoneInfo.ConvertTimeFromUtc(utcTime, tz).ToString("yyyy-MM-dd HH:mm:ss 'PKT'");
        }
        catch
        {
            return utcTime.AddHours(5).ToString("yyyy-MM-dd HH:mm:ss 'PKT'");
        }
    }
}
