using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Email;
using JYCareerAgent.Api.Services.Storage;
using Microsoft.AspNetCore.Mvc;

namespace JYCareerAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly IJsonStorageService _storageService;
    private readonly IEmailReportingService _emailService;
    private readonly IConfiguration _configuration;

    public SettingsController(
        IJsonStorageService storageService,
        IEmailReportingService emailService,
        IConfiguration configuration)
    {
        _storageService = storageService;
        _emailService = emailService;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var settings = await _storageService.LoadAsync<AgentSettings>("data/settings.json", cancellationToken) 
            ?? new AgentSettings();

        // Check secret presence without exposing the actual secret values
        var hasSmtpPassword = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JY_EMAIL__SMTP_PASSWORD")) ||
                              !string.IsNullOrWhiteSpace(_configuration["JY_EMAIL:SMTP_PASSWORD"]);
        var hasAiApiKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JY_AI__API_KEY")) ||
                          !string.IsNullOrWhiteSpace(_configuration["JY_AI:API_KEY"]);

        var response = new
        {
            settings.OperatingMode,
            settings.MaxDailyApplications,
            settings.MatchWeights,
            settings.Thresholds,
            settings.SearchKeywords,
            settings.SearchLocations,
            settings.Schedule,
            settings.Email,
            settings.Ai,
            settings.Sources,
            settings.Backup,
            SecretStatus = new
            {
                HasSmtpPassword = hasSmtpPassword,
                HasAiApiKey = hasAiApiKey
            }
        };

        return Ok(response);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateSettings([FromBody] AgentSettings settings, CancellationToken cancellationToken)
    {
        await _storageService.SaveAsync("data/settings.json", settings, createBackup: true, cancellationToken);
        return Ok(settings);
    }

    [HttpPost("email/test")]
    public async Task<IActionResult> SendTestEmail([FromBody] TestEmailRequest? request, CancellationToken cancellationToken)
    {
        var (success, message) = await _emailService.SendTestEmailAsync(request?.RecipientEmail, cancellationToken);
        return Ok(new { success, message });
    }
}

public class TestEmailRequest
{
    public string? RecipientEmail { get; set; }
}
