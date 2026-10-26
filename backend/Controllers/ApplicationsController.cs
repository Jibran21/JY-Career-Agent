using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Applications;
using Microsoft.AspNetCore.Mvc;

namespace JYCareerAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApplicationsController : ControllerBase
{
    private readonly IApplicationWorkflowService _workflowService;

    public ApplicationsController(IApplicationWorkflowService workflowService)
    {
        _workflowService = workflowService;
    }

    [HttpGet]
    public async Task<IActionResult> GetApplications(
        [FromQuery] ApplicationStatus? status,
        [FromQuery] string? platform,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var applications = await _workflowService.GetApplicationsAsync(cancellationToken);
        var filtered = applications.AsEnumerable();

        if (status.HasValue)
        {
            filtered = filtered.Where(a => a.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(platform))
        {
            filtered = filtered.Where(a => string.Equals(a.Platform, platform, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            filtered = filtered.Where(a => a.Employer.ToLowerInvariant().Contains(s) ||
                                           a.PositionTitle.ToLowerInvariant().Contains(s));
        }

        var results = filtered.OrderByDescending(a => a.ApplicationStartTimeUtc).ToList();
        return Ok(results);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetApplicationById(string id, CancellationToken cancellationToken)
    {
        var app = await _workflowService.GetApplicationByIdAsync(id, cancellationToken);
        if (app == null) return NotFound();
        return Ok(app);
    }

    [HttpPost("{id}/submit")]
    public async Task<IActionResult> SubmitApplication(string id, CancellationToken cancellationToken)
    {
        try
        {
            var app = await _workflowService.SubmitApplicationAsync(id, userExplicitApproval: true, cancellationToken);
            return Ok(app);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(
        string id, 
        [FromBody] UpdateStatusRequest request, 
        CancellationToken cancellationToken)
    {
        try
        {
            var app = await _workflowService.UpdateApplicationStatusAsync(
                id, 
                request.Status, 
                request.Reason ?? "Manual status update via dashboard.", 
                "User", 
                cancellationToken);
            return Ok(app);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}/content")]
    public async Task<IActionResult> UpdateContent(
        string id, 
        [FromBody] UpdateContentRequest request, 
        CancellationToken cancellationToken)
    {
        try
        {
            var app = await _workflowService.UpdateApplicationContentAsync(id, request.CoverLetterText, cancellationToken);
            return Ok(app);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

public class UpdateStatusRequest
{
    public ApplicationStatus Status { get; set; }
    public string? Reason { get; set; }
}

public class UpdateContentRequest
{
    public string CoverLetterText { get; set; } = string.Empty;
}
