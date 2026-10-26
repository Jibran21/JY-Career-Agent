using System.Text.RegularExpressions;
using JYCareerAgent.Api.Models;

namespace JYCareerAgent.Api.Services.Matching;

public class JobMatchingEngine : IJobMatchingEngine
{
    private readonly ILogger<JobMatchingEngine> _logger;

    public JobMatchingEngine(ILogger<JobMatchingEngine> logger)
    {
        _logger = logger;
    }

    public MatchScoreResult EvaluateJob(JobListing job, CandidateProfile profile, AgentSettings settings)
    {
        var text = $"{job.Title} {job.Description} {string.Join(" ", job.RequiredSkills)}";

        // 1. .NET / C# Relevance (0 - 100)
        var dotNetKeywords = new[] { ".NET", "C#", "ASP.NET", ".NET Core", "Entity Framework", "EF Core", "Web API", "LINQ", "DotNet" };
        var dotNetMatches = dotNetKeywords.Where(k => ContainsWord(text, k)).ToList();
        var dotNetScore = dotNetMatches.Count switch
        {
            >= 4 => 100.0,
            >= 2 => 85.0,
            1 => 60.0,
            _ => (ContainsWord(text, "backend") || ContainsWord(text, "API")) ? 30.0 : 0.0
        };

        // 2. Angular / Frontend Relevance (0 - 100)
        var angularKeywords = new[] { "Angular", "TypeScript", "RxJS", "AngularJS", "JavaScript", "HTML5", "CSS3", "Bootstrap", "Ant Design" };
        var angularMatches = angularKeywords.Where(k => ContainsWord(text, k)).ToList();
        var angularScore = angularMatches.Count switch
        {
            >= 3 => 100.0,
            >= 2 => 85.0,
            1 => 60.0,
            _ => (ContainsWord(text, "Frontend") || ContainsWord(text, "Full Stack")) ? 35.0 : 0.0
        };

        // 3. Seniority & Experience Fit (0 - 100)
        // Candidate has ~10 years experience
        var seniorityScore = 70.0;
        var seniorityFit = "Mid-Senior Level Fit";

        if (ContainsWord(text, "Senior") || ContainsWord(text, "Lead") || ContainsWord(text, "Principal") || ContainsWord(text, "Architect"))
        {
            seniorityScore = 100.0;
            seniorityFit = "Excellent Senior/Lead Fit (~10 Years Experience)";
        }
        else if (ContainsWord(text, "Mid") || ContainsWord(text, "Intermediate"))
        {
            seniorityScore = 85.0;
            seniorityFit = "Strong Fit for Mid-Senior Requirement";
        }
        else if (ContainsWord(text, "Junior") || ContainsWord(text, "Intern") || ContainsWord(text, "Entry"))
        {
            seniorityScore = 30.0;
            seniorityFit = "Overqualified (Junior/Intern level)";
        }
        else if (job.ExperienceRequirements.Contains("10") || job.ExperienceRequirements.Contains("8") || job.ExperienceRequirements.Contains("7") || job.ExperienceRequirements.Contains("5"))
        {
            seniorityScore = 100.0;
            seniorityFit = "Direct Match for 5-10+ Years Experience Requirement";
        }

        // 4. SQL / Data Engineering Relevance (0 - 100)
        var sqlKeywords = new[] { "SQL Server", "T-SQL", "Stored Procedure", "SSIS", "ETL", "Power BI", "SSRS", "Database", "Data Warehouse", "Query Optimization" };
        var sqlMatches = sqlKeywords.Where(k => ContainsWord(text, k)).ToList();
        var sqlScore = sqlMatches.Count switch
        {
            >= 3 => 100.0,
            >= 2 => 85.0,
            1 => 65.0,
            _ => ContainsWord(text, "Data") ? 35.0 : 10.0
        };

        // 5. Industry / Domain Relevance (0 - 100)
        // Candidate has rich experience in FinTech, Healthcare, Supply Chain, ERP, AI
        var domainKeywords = new[] { "FinTech", "Banking", "Financial", "Healthcare", "Hospital", "Supply Chain", "ERP", "Inventory", "AI", "Agent", "LLM" };
        var domainMatches = domainKeywords.Where(k => ContainsWord(text, k)).ToList();
        var industryScore = domainMatches.Count switch
        {
            >= 2 => 100.0,
            1 => 80.0,
            _ => 50.0 // General enterprise software is still neutral/positive
        };

        // 6. Location and Work Arrangement (0 - 100)
        var locationScore = 50.0;
        var locationFit = "Evaluating Location Suitability";
        var locLower = $"{job.Location} {job.Description}".ToLowerInvariant();

        if (locLower.Contains("karachi"))
        {
            locationScore = 100.0;
            locationFit = "Direct Local Match (Karachi, Pakistan)";
        }
        else if (job.WorkArrangement == WorkArrangement.Remote || locLower.Contains("remote"))
        {
            if (locLower.Contains("us only") || locLower.Contains("usa only") || locLower.Contains("must reside in us") || locLower.Contains("uk only"))
            {
                locationScore = 20.0;
                locationFit = "Remote but restricted by regional jurisdiction";
            }
            else
            {
                locationScore = 95.0;
                locationFit = "Remote (Pakistan-eligible or Worldwide)";
            }
        }
        else if (locLower.Contains("pakistan") || locLower.Contains("islamabad") || locLower.Contains("lahore") || locLower.Contains("rawalpindi"))
        {
            locationScore = 85.0;
            locationFit = "Pakistan Domestic Opportunity";
        }
        else if (locLower.Contains("relocation") || locLower.Contains("visa sponsorship"))
        {
            locationScore = 80.0;
            locationFit = "International with Relocation / Sponsorship";
        }

        // Weighted Calculation
        var weights = settings.MatchWeights;
        var totalWeight = weights.DotNetWeight + weights.AngularWeight + weights.SeniorityWeight +
                           weights.SqlWeight + weights.IndustryWeight + weights.LocationWeight;

        if (totalWeight <= 0) totalWeight = 100;

        var weightedSum = (dotNetScore * weights.DotNetWeight) +
                          (angularScore * weights.AngularWeight) +
                          (seniorityScore * weights.SeniorityWeight) +
                          (sqlScore * weights.SqlWeight) +
                          (industryScore * weights.IndustryWeight) +
                          (locationScore * weights.LocationWeight);

        var totalScore = Math.Round(weightedSum / totalWeight, 1);

        // Matching skills vs Missing skills
        var allCandidateSkills = profile.Skills.AllSkills.ToList();
        var candidateSkillSet = new HashSet<string>(allCandidateSkills, StringComparer.OrdinalIgnoreCase);

        var matchingSkills = new List<string>();
        foreach (var skill in allCandidateSkills)
        {
            if (ContainsWord(text, skill))
            {
                matchingSkills.Add(skill);
            }
        }

        // Missing skills
        var missingSkills = new List<string>();
        var commonOtherTechs = new[] { "AWS", "Docker", "Kubernetes", "Microservices", "Kafka", "RabbitMQ", "GraphQL", "Python", "Go", "Java", "Redis" };
        foreach (var other in commonOtherTechs)
        {
            if (ContainsWord(text, other) && !candidateSkillSet.Contains(other))
            {
                missingSkills.Add(other);
            }
        }

        // Reasons to apply
        var reasons = new List<string>();
        if (dotNetScore >= 80) reasons.Add("Strong alignment with core .NET Core / C# enterprise experience.");
        if (angularScore >= 80) reasons.Add("Direct match with Angular and TypeScript frontend proficiency.");
        if (sqlScore >= 80) reasons.Add("Leverages deep MS SQL Server, SSIS, and data architecture background.");
        if (seniorityScore >= 85) reasons.Add("Candidate's 10-year engineering tenure matches requested seniority.");
        if (industryScore >= 80) reasons.Add($"Relevant industry domain expertise ({string.Join(", ", domainMatches)}).");
        if (locationScore >= 85) reasons.Add($"Favorable geographic/work setup ({locationFit}).");

        // Concerns
        var concerns = new List<string>();
        if (dotNetScore < 50 && angularScore < 50) concerns.Add("Job does not heavily focus on candidate's primary .NET or Angular stack.");
        if (seniorityScore < 50) concerns.Add("Role seniority level may not align with a Senior 10-year engineer.");
        if (locationScore <= 30) concerns.Add("Location or regional remote restriction may pose work authorization barrier.");
        if (missingSkills.Any()) concerns.Add($"Job requests skills outside primary verified profile: {string.Join(", ", missingSkills.Take(3))}.");

        // Recommendation
        string recommendation;
        if (totalScore >= settings.Thresholds.HighPriority)
        {
            recommendation = "HighPriority";
        }
        else if (totalScore >= settings.Thresholds.ApprovalQueue)
        {
            recommendation = "ApprovalQueue";
        }
        else
        {
            recommendation = "Skip";
        }

        var result = new MatchScoreResult
        {
            TotalScore = totalScore,
            DotNetScore = dotNetScore,
            AngularScore = angularScore,
            SeniorityScore = seniorityScore,
            SqlScore = sqlScore,
            IndustryScore = industryScore,
            LocationScore = locationScore,
            MatchingSkills = matchingSkills.Distinct().ToList(),
            MissingSkills = missingSkills.Distinct().ToList(),
            SeniorityFit = seniorityFit,
            LocationFit = locationFit,
            ReasonsToApply = reasons,
            PotentialConcerns = concerns,
            Recommendation = recommendation,
            Summary = $"Match Score: {totalScore:F1}/100 ({recommendation}). .NET: {dotNetScore:F0}%, Angular: {angularScore:F0}%, Seniority: {seniorityScore:F0}%, SQL: {sqlScore:F0}%, Domain: {industryScore:F0}%, Location: {locationScore:F0}%."
        };

        return result;
    }

    private static bool ContainsWord(string text, string word)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(word)) return false;
        // Handle special characters like C# and .NET cleanly
        var pattern = $@"(?i)(?:^|[^\w#+]){Regex.Escape(word)}(?:[^\w#+]|$)";
        return Regex.IsMatch(text, pattern);
    }
}
