using System.Text.Json;
using JYCareerAgent.Api.Models;

namespace JYCareerAgent.Api.Services.JobSources;

public class ArbeitnowJobSource : IJobSource
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ArbeitnowJobSource> _logger;

    public string SourceName => "Arbeitnow";
    public string AccessType => "Official Public API";

    public ArbeitnowJobSource(HttpClient httpClient, ILogger<ArbeitnowJobSource> logger)
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
            var url = "https://www.arbeitnow.com/api/job-board-api";
            _logger.LogInformation("Querying Arbeitnow API: {Url}", url);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "JYCareerAgent/1.0 (Portfolio Job Search System; contact: jibrankhalily@gmail.com)");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                result.Success = false;
                result.ErrorMessage = $"Arbeitnow API returned HTTP {response.StatusCode}";
                return result;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                foreach (var item in dataElement.EnumerateArray())
                {
                    var slug = item.TryGetProperty("slug", out var pSlug) ? pSlug.GetString() ?? "" : Guid.NewGuid().ToString("N");
                    var title = item.TryGetProperty("title", out var pTitle) ? pTitle.GetString() ?? "" : "";
                    var company = item.TryGetProperty("company_name", out var pComp) ? pComp.GetString() ?? "" : "";
                    var jobUrl = item.TryGetProperty("url", out var pUrl) ? pUrl.GetString() ?? "" : "";
                    var desc = item.TryGetProperty("description", out var pDesc) ? pDesc.GetString() ?? "" : "";
                    var location = item.TryGetProperty("location", out var pLoc) ? pLoc.GetString() ?? "Remote" : "Remote";
                    var isRemote = item.TryGetProperty("remote", out var pRem) && pRem.GetBoolean();

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
                        PlatformJobId = slug,
                        SourcePlatform = SourceName,
                        Title = title,
                        Employer = company,
                        JobUrl = jobUrl,
                        Description = StripHtml(desc),
                        Location = location,
                        WorkArrangement = isRemote ? WorkArrangement.Remote : WorkArrangement.Hybrid,
                        RequiredSkills = tags,
                        ApplicationMethod = "Direct"
                    };

                    result.Jobs.Add(job);
                }
            }

            result.Success = true;
            _logger.LogInformation("Arbeitnow API returned {Count} jobs.", result.Jobs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching from Arbeitnow API");
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
