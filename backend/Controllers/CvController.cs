using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.CV;
using Microsoft.AspNetCore.Mvc;

namespace JYCareerAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CvController : ControllerBase
{
    private readonly ICvManagerService _cvManagerService;

    public CvController(ICvManagerService cvManagerService)
    {
        _cvManagerService = cvManagerService;
    }

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var profile = await _cvManagerService.GetProfileAsync(cancellationToken);
        return Ok(profile);
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] CandidateProfile profile, CancellationToken cancellationToken)
    {
        await _cvManagerService.UpdateProfileAsync(profile, cancellationToken);
        return Ok(profile);
    }

    [HttpGet("master")]
    public async Task<IActionResult> GetMasterCv(CancellationToken cancellationToken)
    {
        var text = await _cvManagerService.GetMasterCvTextAsync(cancellationToken);
        return Ok(new { content = text });
    }

    [HttpGet("tailored")]
    public async Task<IActionResult> ListTailoredCvs(CancellationToken cancellationToken)
    {
        var list = await _cvManagerService.ListTailoredCvsAsync(cancellationToken);
        return Ok(list);
    }

    [HttpGet("tailored/{fileName}/download")]
    public async Task<IActionResult> DownloadTailoredCv(string fileName, CancellationToken cancellationToken)
    {
        var content = await _cvManagerService.GetTailoredCvContentAsync(fileName, cancellationToken);
        if (content == null) return NotFound();

        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return File(bytes, "text/plain", fileName);
    }
}
