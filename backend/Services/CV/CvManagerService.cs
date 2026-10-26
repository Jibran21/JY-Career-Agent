using System.Text;
using System.Text.RegularExpressions;
using JYCareerAgent.Api.Models;
using JYCareerAgent.Api.Services.Storage;

namespace JYCareerAgent.Api.Services.CV;

public class CvManagerService : ICvManagerService
{
    private readonly IJsonStorageService _storageService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CvManagerService> _logger;
    private readonly string _baseRoot;

    public CvManagerService(
        IJsonStorageService storageService,
        IConfiguration configuration,
        ILogger<CvManagerService> logger)
    {
        _storageService = storageService;
        _configuration = configuration;
        _logger = logger;

        var configuredRoot = configuration["JY_STORAGE_ROOT"];
        var root = !string.IsNullOrWhiteSpace(configuredRoot) 
            ? configuredRoot 
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        if (!Directory.Exists(Path.Combine(root, "data")) && Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "data")))
        {
            root = Directory.GetCurrentDirectory();
        }
        else if (!Directory.Exists(Path.Combine(root, "data")))
        {
            var parent = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName;
            if (parent != null && Directory.Exists(Path.Combine(parent, "data")))
            {
                root = parent;
            }
        }

        _baseRoot = root;
    }

    public async Task<string> GetMasterCvTextAsync(CancellationToken cancellationToken = default)
    {
        var profile = await GetProfileAsync(cancellationToken);
        var relPath = profile.MasterCvPath;
        var fullPath = Path.Combine(_baseRoot, relPath);

        if (!File.Exists(fullPath))
        {
            // Fallback search
            var altPath = Path.Combine(_baseRoot, "cv", "master-cv", "Jibran_Yousuf_Khan_CV.txt");
            if (File.Exists(altPath))
            {
                fullPath = altPath;
            }
            else
            {
                _logger.LogWarning("Master CV file not found at {FullPath}. Generating from verified profile.json.", fullPath);
                return GenerateTextCvFromProfile(profile);
            }
        }

        return await File.ReadAllTextAsync(fullPath, cancellationToken);
    }

    public async Task<CandidateProfile> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var profile = await _storageService.LoadAsync<CandidateProfile>("data/profile.json", cancellationToken);
        if (profile == null)
        {
            _logger.LogWarning("profile.json not found or empty. Initializing default verified profile.");
            profile = new CandidateProfile();
            await _storageService.SaveAsync("data/profile.json", profile, createBackup: false, cancellationToken);
        }
        return profile;
    }

    public async Task UpdateProfileAsync(CandidateProfile profile, CancellationToken cancellationToken = default)
    {
        await _storageService.SaveAsync("data/profile.json", profile, createBackup: true, cancellationToken);
        _logger.LogInformation("Profile updated and backup created.");
    }

    public async Task<(string fileName, string fullPath, string content)> GenerateTailoredCvAsync(
        JobListing job, 
        CandidateProfile profile, 
        CancellationToken cancellationToken = default)
    {
        var safeCompany = Regex.Replace(job.Employer ?? "Company", @"[^a-zA-Z0-9]", "");
        var safeJobId = Regex.Replace(job.Id, @"[^a-zA-Z0-9]", "");
        if (safeJobId.Length > 8) safeJobId = safeJobId[..8];
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"Jibran_Yousuf_Khan_CV_{safeCompany}_{safeJobId}_{timestamp}.txt";

        var tailoredDir = Path.Combine(_baseRoot, "cv", "tailored");
        Directory.CreateDirectory(tailoredDir);
        var fullPath = Path.Combine(tailoredDir, fileName);

        // ATS Tailored Generation:
        // Strictly maintains candidate's actual qualifications while reordering, formatting, and highlighting matching competencies
        var content = BuildAtsOptimizedCv(job, profile);

        await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8, cancellationToken);
        _logger.LogInformation("Generated tailored CV: {FileName} for Job ID: {JobId} ({Employer})", fileName, job.Id, job.Employer);

        return (fileName, fullPath, content);
    }

    public Task<List<TailoredCvMetadata>> ListTailoredCvsAsync(CancellationToken cancellationToken = default)
    {
        var tailoredDir = Path.Combine(_baseRoot, "cv", "tailored");
        if (!Directory.Exists(tailoredDir))
            return Task.FromResult(new List<TailoredCvMetadata>());

        var list = new List<TailoredCvMetadata>();
        var files = Directory.GetFiles(tailoredDir, "*.txt");

        foreach (var file in files)
        {
            var fileInfo = new FileInfo(file);
            var name = Path.GetFileName(file);
            
            // Format: Jibran_Yousuf_Khan_CV_{Company}_{JobId}_{timestamp}.txt
            var parts = name.Split('_');
            var employer = parts.Length > 4 ? parts[4] : "General";
            var jobId = parts.Length > 5 ? parts[5] : "";

            list.Add(new TailoredCvMetadata
            {
                FileName = name,
                Employer = employer,
                JobId = jobId,
                CreatedAtUtc = fileInfo.CreationTimeUtc,
                FileSizeBytes = fileInfo.Length
            });
        }

        list = list.OrderByDescending(x => x.CreatedAtUtc).ToList();
        return Task.FromResult(list);
    }

    public async Task<string?> GetTailoredCvContentAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var safeFileName = Path.GetFileName(fileName);
        var fullPath = Path.Combine(_baseRoot, "cv", "tailored", safeFileName);
        if (!File.Exists(fullPath)) return null;
        return await File.ReadAllTextAsync(fullPath, cancellationToken);
    }

    public Task<string> GetTailoredCvPathAsync(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        var fullPath = Path.Combine(_baseRoot, "cv", "tailored", safeFileName);
        return Task.FromResult(fullPath);
    }

    private string BuildAtsOptimizedCv(JobListing job, CandidateProfile profile)
    {
        var sb = new StringBuilder();

        sb.AppendLine("==========================================================================");
        sb.AppendLine($"                    {profile.FullName.ToUpper()}");
        sb.AppendLine($"      {profile.Headline}");
        sb.AppendLine($" {profile.Location} | {profile.Email} | {profile.Phone}");
        sb.AppendLine("==========================================================================");
        sb.AppendLine();

        // 1. Target Alignment & Professional Summary
        sb.AppendLine("PROFESSIONAL SUMMARY");
        sb.AppendLine("--------------------------------------------------------------------------");
        
        var isDotNet = job.Title.Contains(".NET", StringComparison.OrdinalIgnoreCase) || 
                       job.Description.Contains(".NET", StringComparison.OrdinalIgnoreCase) ||
                       job.Description.Contains("C#", StringComparison.OrdinalIgnoreCase);
        var isAngular = job.Title.Contains("Angular", StringComparison.OrdinalIgnoreCase) || 
                        job.Description.Contains("Angular", StringComparison.OrdinalIgnoreCase);
        var isData = job.Title.Contains("Data", StringComparison.OrdinalIgnoreCase) || 
                     job.Title.Contains("SSIS", StringComparison.OrdinalIgnoreCase) || 
                     job.Description.Contains("ETL", StringComparison.OrdinalIgnoreCase);
        var isAi = job.Title.Contains("AI", StringComparison.OrdinalIgnoreCase) || 
                   job.Description.Contains("Agent", StringComparison.OrdinalIgnoreCase) ||
                   job.Description.Contains("LLM", StringComparison.OrdinalIgnoreCase);

        var focusAreas = new List<string>();
        if (isDotNet) focusAreas.Add(".NET Core / ASP.NET Core & C# enterprise architecture");
        if (isAngular) focusAreas.Add("modern Angular single-page applications & responsive UI");
        if (isData) focusAreas.Add("high-throughput SQL Server & SSIS ETL pipelines");
        if (isAi) focusAreas.Add("AI-agent workflows & LLM orchestration");

        var focusString = focusAreas.Any() ? string.Join(", ", focusAreas) : "Full-Stack .NET & Angular engineering";

        sb.AppendLine($"Versatile Senior Data & Software Engineer with approximately 10 years of experience delivering high-performance, scalable enterprise systems across FinTech, Healthcare, and ERP domains. Specialized in {focusString}. Experienced in architecting secure RESTful APIs, multi-threaded data processors, and resilient distributed solutions. Strictly committed to Clean Architecture, asynchronous performance patterns, and maintainable software engineering.");
        sb.AppendLine();

        // 2. Technical Skills (prioritize matching skills)
        sb.AppendLine("CORE TECHNICAL COMPETENCIES");
        sb.AppendLine("--------------------------------------------------------------------------");
        
        var matchingSkills = job.MatchScore?.MatchingSkills ?? new List<string>();
        if (matchingSkills.Any())
        {
            sb.AppendLine($"Target Matching Skills: {string.Join(", ", matchingSkills.Take(12))}");
        }

        sb.AppendLine($"Languages & Frameworks: {string.Join(", ", profile.Skills.LanguagesAndFrameworks)}");
        sb.AppendLine($"Database & Reporting:   {string.Join(", ", profile.Skills.DatabaseAndReporting)}");
        sb.AppendLine($"Architecture & Design:  {string.Join(", ", profile.Skills.ArchitectureAndMessaging)}");
        sb.AppendLine($"AI & Agentic Systems:   {string.Join(", ", profile.Skills.AiAndAgentic)}");
        sb.AppendLine($"Tools & DevOps:         {string.Join(", ", profile.Skills.ToolsAndDevOps)}");
        sb.AppendLine($"Specialties:            {string.Join(", ", profile.Skills.Specialties)}");
        sb.AppendLine();

        // 3. AI & Agentic Development
        sb.AppendLine("AI & AGENTIC SYSTEMS FOCUS");
        sb.AppendLine("--------------------------------------------------------------------------");
        sb.AppendLine("• Designing AI-agent workflows combining LLMs with tool/function calling, structured tasks, and human approval.");
        sb.AppendLine("• Hands-on exploration of Microsoft Semantic Kernel and Microsoft Agent Framework in the .NET ecosystem.");
        sb.AppendLine("• Planning AI-enabled systems with .NET, Angular, and SQL Server using Retrieval-Augmented Generation (RAG).");
        sb.AppendLine();

        // 4. Professional Experience
        sb.AppendLine("PROFESSIONAL EXPERIENCE");
        sb.AppendLine("--------------------------------------------------------------------------");
        foreach (var exp in profile.Experience)
        {
            sb.AppendLine($"{exp.Title.ToUpper()} | {exp.Company} ({exp.Period})");
            sb.AppendLine($"Location: {exp.Location}");
            foreach (var hl in exp.Highlights)
            {
                sb.AppendLine($"  • {hl}");
            }
            sb.AppendLine();
        }

        // 5. Education & Credentials
        sb.AppendLine("EDUCATION & ACADEMIC CREDENTIALS");
        sb.AppendLine("--------------------------------------------------------------------------");
        foreach (var edu in profile.Education)
        {
            sb.AppendLine($"• {edu.Degree} — {edu.Institution} ({edu.Year}) [Details: {edu.Details}]");
        }
        sb.AppendLine();

        sb.AppendLine("==========================================================================");
        sb.AppendLine($"Tailored for: {job.Employer} - {job.Title} | Verified Master Profile Reference");
        sb.AppendLine("==========================================================================");

        return sb.ToString();
    }

    private string GenerateTextCvFromProfile(CandidateProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine(profile.FullName);
        sb.AppendLine(profile.Headline);
        sb.AppendLine($"{profile.Location} | {profile.Email} | {profile.Phone}");
        sb.AppendLine();
        sb.AppendLine("PROFESSIONAL SUMMARY");
        sb.AppendLine(profile.Summary);
        sb.AppendLine();
        sb.AppendLine("EXPERIENCE");
        foreach (var exp in profile.Experience)
        {
            sb.AppendLine($"{exp.Title} - {exp.Company} ({exp.Period})");
            foreach (var h in exp.Highlights)
            {
                sb.AppendLine($"• {h}");
            }
            sb.AppendLine();
        }
        sb.AppendLine("EDUCATION");
        foreach (var edu in profile.Education)
        {
            sb.AppendLine($"• {edu.Degree} - {edu.Institution} ({edu.Year})");
        }
        return sb.ToString();
    }
}
