using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Applications;
using JYCareerAgent.Api.Services.CV;
using JYCareerAgent.Api.Services.Storage;
using Microsoft.AspNetCore.Mvc;

namespace JYCareerAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly IJsonStorageService _storageService;
    private readonly IApplicationWorkflowService _workflowService;
    private readonly ICvManagerService _cvManagerService;

    public JobsController(
        IJsonStorageService storageService,
        IApplicationWorkflowService workflowService,
        ICvManagerService cvManagerService)
    {
        _storageService = storageService;
        _workflowService = workflowService;
        _cvManagerService = cvManagerService;
    }

    [HttpGet]
    public async Task<IActionResult> GetJobs(
        [FromQuery] string? query,
        [FromQuery] string? source,
        [FromQuery] double? minScore,
        [FromQuery] bool? onlyUnreviewed,
        CancellationToken cancellationToken)
    {
        var jobs = await _storageService.LoadAsync<List<JobListing>>("data/job-history.json", cancellationToken) 
            ?? new List<JobListing>();

        var filtered = jobs.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLowerInvariant();
            filtered = filtered.Where(j => j.Title.ToLowerInvariant().Contains(q) ||
                                           j.Employer.ToLowerInvariant().Contains(q) ||
                                           j.Location.ToLowerInvariant().Contains(q) ||
                                           j.Description.ToLowerInvariant().Contains(q));
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            filtered = filtered.Where(j => string.Equals(j.SourcePlatform, source, StringComparison.OrdinalIgnoreCase));
        }

        if (minScore.HasValue)
        {
            filtered = filtered.Where(j => j.MatchScore != null && j.MatchScore.TotalScore >= minScore.Value);
        }

        if (onlyUnreviewed == true)
        {
            filtered = filtered.Where(j => !j.IsReviewed && !j.IsSkipped);
        }

        var results = filtered.OrderByDescending(j => j.MatchScore?.TotalScore ?? 0).ToList();
        return Ok(results);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetJobById(string id, CancellationToken cancellationToken)
    {
        var jobs = await _storageService.LoadAsync<List<JobListing>>("data/job-history.json", cancellationToken) 
            ?? new List<JobListing>();
        var job = jobs.FirstOrDefault(j => j.Id == id);
        if (job == null) return NotFound();
        return Ok(job);
    }

    [HttpPost("{id}/prepare")]
    public async Task<IActionResult> PrepareApplication(string id, CancellationToken cancellationToken)
    {
        var jobs = await _storageService.LoadAsync<List<JobListing>>("data/job-history.json", cancellationToken) 
            ?? new List<JobListing>();
        var job = jobs.FirstOrDefault(j => j.Id == id);
        if (job == null) return NotFound(new { message = $"Job {id} not found." });

        var profile = await _cvManagerService.GetProfileAsync(cancellationToken);
        var settings = await _storageService.LoadAsync<AgentSettings>("data/settings.json", cancellationToken) ?? new AgentSettings();

        var app = await _workflowService.PrepareApplicationAsync(job, profile, settings, cancellationToken);
        
        job.IsReviewed = true;
        await _storageService.SaveAsync("data/job-history.json", jobs, createBackup: false, cancellationToken);

        return Ok(app);
    }

    [HttpPost("{id}/skip")]
    public async Task<IActionResult> SkipJob(string id, [FromBody] SkipJobRequest request, CancellationToken cancellationToken)
    {
        var jobs = await _storageService.LoadAsync<List<JobListing>>("data/job-history.json", cancellationToken) 
            ?? new List<JobListing>();
        var job = jobs.FirstOrDefault(j => j.Id == id);
        if (job == null) return NotFound();

        job.IsSkipped = true;
        job.SkipReason = request.Reason ?? "Manually skipped by user.";
        await _storageService.SaveAsync("data/job-history.json", jobs, createBackup: false, cancellationToken);

        return Ok(job);
    }
}

public class SkipJobRequest
{
    public string? Reason { get; set; }
}
