using System.Text.Json;
using JYCareerAgent.Api.Models;

namespace JYCareerAgent.Api.Services.JobSources;

public class RemotiveJobSource : IJobSource
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RemotiveJobSource> _logger;

    public string SourceName => "Remotive";
    public string AccessType => "Official Public API";

    public RemotiveJobSource(HttpClient httpClient, ILogger<RemotiveJobSource> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<JobSourceResult> SearchJobsAsync(AgentSettings settings, CancellationToken cancellationToken = default)
    {
        var result = new JobSourceResult
        {
            SourceName = SourceName,
            AccessType = AccessType
        };

        try
        {
            var url = "https://remotive.com/api/remote-jobs?category=software-dev&limit=30";
            _logger.LogInformation("Querying Remotive API: {Url}", url);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "JYCareerAgent/1.0 (Portfolio Job Search System; contact: jibrankhalily@gmail.com)");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                result.Success = false;
                result.ErrorMessage = $"Remotive API returned HTTP {response.StatusCode}";
                return result;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("jobs", out var jobsElement))
            {
                foreach (var item in jobsElement.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var pId) ? pId.ToString() : Guid.NewGuid().ToString("N");
                    var title = item.TryGetProperty("title", out var pTitle) ? pTitle.GetString() ?? "" : "";
                    var company = item.TryGetProperty("company_name", out var pComp) ? pComp.GetString() ?? "" : "";
                    var jobUrl = item.TryGetProperty("url", out var pUrl) ? pUrl.GetString() ?? "" : "";
                    var desc = item.TryGetProperty("description", out var pDesc) ? pDesc.GetString() ?? "" : "";
                    var location = item.TryGetProperty("candidate_required_location", out var pLoc) ? pLoc.GetString() ?? "Worldwide" : "Worldwide";
                    var salary = item.TryGetProperty("salary", out var pSal) ? pSal.GetString() ?? "" : "";

                    DateTime? posted = null;
                    if (item.TryGetProperty("publication_date", out var pPub) && DateTime.TryParse(pPub.GetString(), out var pubDate))
                    {
                        posted = pubDate;
                    }

                    // Extract tags
                    var tags = new List<string>();
                    if (item.TryGetProperty("tags", out var pTags) && pTags.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tag in pTags.EnumerateArray())
                        {
                            var tStr = tag.GetString();
                            if (!string.IsNullOrWhiteSpace(tStr)) tags.Add(tStr);
                        }
                    }

                    var job = new JobListing
                    {
                        PlatformJobId = id,
                        SourcePlatform = SourceName,
                        Title = title,
                        Employer = company,
                        JobUrl = jobUrl,
                        Description = StripHtml(desc),
                        Location = location,
                        WorkArrangement = WorkArrangement.Remote,
                        SalaryRange = salary,
                        PostedDate = posted,
                        RequiredSkills = tags,
                        ApplicationMethod = "Direct"
                    };

                    result.Jobs.Add(job);
                }
            }

            result.Success = true;
            _logger.LogInformation("Remotive API returned {Count} software jobs.", result.Jobs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching from Remotive API");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var clean = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", " ");
        clean = System.Net.WebUtility.HtmlDecode(clean);
        return System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ").Trim();
    }
}
