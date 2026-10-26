using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Email;
using Microsoft.AspNetCore.Mvc;

namespace JYCareerAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly IEmailReportingService _emailService;

    public ReportsController(IEmailReportingService emailService)
    {
        _emailService = emailService;
    }

    [HttpGet]
    public async Task<IActionResult> GetReports(CancellationToken cancellationToken)
    {
        var reports = await _emailService.GetReportHistoryAsync(cancellationToken);
        return Ok(reports);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetReportById(string id, CancellationToken cancellationToken)
    {
        var reports = await _emailService.GetReportHistoryAsync(cancellationToken);
        var report = reports.FirstOrDefault(r => r.Id == id);
        if (report == null) return NotFound();
        return Ok(report);
    }

    [HttpPost("{id}/resend")]
    public async Task<IActionResult> ResendReport(string id, CancellationToken cancellationToken)
    {
        var reports = await _emailService.GetReportHistoryAsync(cancellationToken);
        var report = reports.FirstOrDefault(r => r.Id == id);
        if (report == null) return NotFound();

        var success = await _emailService.SendDailyReportAsync(report, cancellationToken);
        return Ok(new { success, message = report.DeliveryStatusMessage });
    }
}
