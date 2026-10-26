using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JYCareerAgent.Api.Models;

namespace JYCareerAgent.Api.Services.AI;

public class AiAgentService : IAiAgentService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiAgentService> _logger;
    private readonly string _promptsDir;

    public AiAgentService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AiAgentService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        var configuredRoot = configuration["JY_STORAGE_ROOT"];
        var root = !string.IsNullOrWhiteSpace(configuredRoot) 
            ? configuredRoot 
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        if (!Directory.Exists(Path.Combine(root, "prompts")) && Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "prompts")))
        {
            root = Directory.GetCurrentDirectory();
        }
        else if (!Directory.Exists(Path.Combine(root, "prompts")))
        {
            var parent = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName;
            if (parent != null && Directory.Exists(Path.Combine(parent, "prompts")))
            {
                root = parent;
            }
        }

        _promptsDir = Path.Combine(root, "prompts");
    }

    private async Task<string> LoadPromptTemplateAsync(string fileName, CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(_promptsDir, fileName);
        if (File.Exists(fullPath))
        {
            return await File.ReadAllTextAsync(fullPath, cancellationToken);
        }
        return string.Empty;
    }

    public async Task<string> GenerateCoverLetterAsync(
        JobListing job, 
        CandidateProfile profile, 
        AgentSettings settings, 
        CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["JY_AI__API_KEY"] ?? _configuration["AI:ApiKey"];
        var provider = _configuration["JY_AI__PROVIDER"] ?? settings.Ai.Provider;

        // If external LLM API key is present and provider is OpenAI/Azure/etc., invoke LLM
        if (!string.IsNullOrWhiteSpace(apiKey) && !string.Equals(provider, "TemplateEngine", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var template = await LoadPromptTemplateAsync("cover_letter.txt", cancellationToken);
                var prompt = template
                    .Replace("{{CandidateProfile}}", JsonSerializer.Serialize(profile))
                    .Replace("{{JobCompany}}", job.Employer)
                    .Replace("{{JobTitle}}", job.Title)
                    .Replace("{{JobDescription}}", SanitizeUntrustedText(job.Description));

                var llmResponse = await CallOpenAiCompatibleApiAsync(prompt, settings.Ai, apiKey, cancellationToken);
                if (!string.IsNullOrWhiteSpace(llmResponse))
                {
                    return llmResponse.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "External LLM call failed. Falling back to deterministic high-quality cover letter synthesis.");
            }
        }

        // Deterministic High-Quality Synthesis (Safe, Grounded, No Hallucinations)
        return GenerateDeterministicCoverLetter(job, profile);
    }

    public async Task<string> TailorCvContentAsync(
        JobListing job, 
        CandidateProfile profile, 
        string masterCvText, 
        AgentSettings settings, 
        CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["JY_AI__API_KEY"] ?? _configuration["AI:ApiKey"];
        var provider = _configuration["JY_AI__PROVIDER"] ?? settings.Ai.Provider;

        if (!string.IsNullOrWhiteSpace(apiKey) && !string.Equals(provider, "TemplateEngine", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var template = await LoadPromptTemplateAsync("cv_tailoring.txt", cancellationToken);
                var prompt = template
                    .Replace("{{MasterCV}}", masterCvText)
                    .Replace("{{JobTitle}}", job.Title)
                    .Replace("{{JobCompany}}", job.Employer)
                    .Replace("{{JobDescription}}", SanitizeUntrustedText(job.Description));

                var llmResponse = await CallOpenAiCompatibleApiAsync(prompt, settings.Ai, apiKey, cancellationToken);
                if (!string.IsNullOrWhiteSpace(llmResponse))
                {
                    return llmResponse.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM CV tailoring failed. Falling back to verified ATS layout.");
            }
        }

        return masterCvText;
    }

    public Task<List<ScreeningQuestionAnswer>> EvaluateScreeningQuestionsAsync(
        List<string> questions, 
        CandidateProfile profile, 
        CancellationToken cancellationToken = default)
    {
        var results = new List<ScreeningQuestionAnswer>();

        foreach (var q in questions)
        {
            var qLower = q.ToLowerInvariant();
            var item = new ScreeningQuestionAnswer { Question = q };

            if (qLower.Contains("notice") || qLower.Contains("available") || qLower.Contains("start date"))
            {
                item.Answer = profile.ScreeningAnswers.NoticePeriod;
                item.RequiresApproval = false;
                item.Reason = "Answered from pre-approved profile settings (Notice Period).";
            }
            else if (qLower.Contains("authorized") || qLower.Contains("work authorization") || qLower.Contains("legally"))
            {
                item.Answer = profile.ScreeningAnswers.WorkAuthorization;
                item.RequiresApproval = false;
                item.Reason = "Answered from pre-approved profile settings (Work Authorization).";
            }
            else if (qLower.Contains("relocat"))
            {
                item.Answer = profile.ScreeningAnswers.WillingToRelocate;
                item.RequiresApproval = false;
                item.Reason = "Answered from pre-approved profile settings (Relocation Preferences).";
            }
            else if (qLower.Contains("experience") && (qLower.Contains("years") || qLower.Contains(".net") || qLower.Contains("c#")))
            {
                item.Answer = "Approximately 10 years of professional software engineering experience specializing in C#, .NET Core, Angular, and SQL Server.";
                item.RequiresApproval = false;
                item.Reason = "Answered from verified career timeline (2015–Present).";
            }
            else if (qLower.Contains("salary") || qLower.Contains("compensation") || qLower.Contains("expectation"))
            {
                item.Answer = profile.ScreeningAnswers.TargetCompensation;
                item.RequiresApproval = true;
                item.Reason = "Compensation discussions require explicit candidate confirmation.";
            }
            else
            {
                item.Answer = "Pending candidate review.";
                item.RequiresApproval = true;
                item.Reason = "Custom screening question requires explicit human verification.";
            }

            results.Add(item);
        }

        return Task.FromResult(results);
    }

    private string GenerateDeterministicCoverLetter(JobListing job, CandidateProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Date: {DateTime.UtcNow:MMMM dd, yyyy}");
        sb.AppendLine($"Hiring Team, {job.Employer}");
        sb.AppendLine($"Re: Application for {job.Title}");
        sb.AppendLine();
        sb.AppendLine("Dear Hiring Manager,");
        sb.AppendLine();
        sb.AppendLine($"I am writing to express my strong interest in the {job.Title} role at {job.Employer}. With approximately 10 years of hands-on software engineering experience specializing in C#, .NET Core, ASP.NET Web API, Angular, and high-performance MS SQL Server architectures, I have delivered resilient, scalable solutions across FinTech, digital banking, healthcare ERP, and supply chain operations.");
        sb.AppendLine();

        // Highlight matching projects based on job requirements
        if (job.Title.Contains("Data", StringComparison.OrdinalIgnoreCase) || 
            job.Description.Contains("ETL", StringComparison.OrdinalIgnoreCase) ||
            job.Description.Contains("SSIS", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("Throughout my career, I have specialized in large-scale data systems. At Think Enabled, I architected and maintained SSIS ETL pipelines feeding real-time Power BI dashboards and executive analytics, introducing incremental data-loading models that significantly reduced processing overhead. At Smart Works Systems, I conducted deep T-SQL query and execution-plan optimization across large relational databases.");
        }
        else if (job.Description.Contains("Bank", StringComparison.OrdinalIgnoreCase) || 
                 job.Description.Contains("FinTech", StringComparison.OrdinalIgnoreCase) ||
                 job.Description.Contains("Financial", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("In the financial technology domain at Aion Digital, I engineered core digital banking solutions for retail, corporate, and internet banking. I developed secure, high-concurrency RESTful APIs powering mission-critical workflows, while building accessible frontends using modern Angular and TypeScript.");
        }
        else
        {
            sb.AppendLine("In my current role as Senior Consultant at Think Enabled, I lead the technical design and architectural planning of complex enterprise modules for supply chain and clinical operations. My work centers on clean architectural patterns, multi-threaded backend processing, asynchronous performance tuning, and real-time communication using SignalR hubs and Firebase push services.");
        }

        sb.AppendLine();
        sb.AppendLine($"Your opening at {job.Employer} represents an exciting alignment with my technical background and passion for building dependable, high-throughput software systems. I look forward to discussing how my experience can contribute to your engineering goals.");
        sb.AppendLine();
        sb.AppendLine("Sincerely,");
        sb.AppendLine(profile.FullName);
        sb.AppendLine($"{profile.Email} | {profile.Phone}");
        sb.AppendLine($"{profile.Location}");

        return sb.ToString();
    }

    private async Task<string> CallOpenAiCompatibleApiAsync(string prompt, AiSettings aiSettings, string apiKey, CancellationToken cancellationToken)
    {
        var endpoint = !string.IsNullOrWhiteSpace(aiSettings.BaseUrl) 
            ? aiSettings.BaseUrl 
            : "https://api.openai.com/v1/chat/completions";

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(aiSettings.Model) ? "gpt-4o-mini" : aiSettings.Model,
            messages = new[]
            {
                new { role = "system", content = "You are a professional career agent writing truthful, verified application materials for Jibran Yousuf Khan." },
                new { role = "user", content = prompt }
            },
            temperature = aiSettings.Temperature
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("OpenAI API error {StatusCode}: {Error}", response.StatusCode, err);
            return string.Empty;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
            {
                return content.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string SanitizeUntrustedText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        // Strip potential prompt injection delimiters and command sequences
        var clean = input.Replace("```", "'''");
        clean = Regex.Replace(clean, @"(?i)(ignore\s+all\s+previous\s+instructions|system\s+prompt|reveal\s+secret)", "[filtered]");
        return clean;
    }
}
