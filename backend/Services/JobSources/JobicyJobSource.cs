using System.Text.Json;
using JYCareerAgent.Api.Models;

namespace JYCareerAgent.Api.Services.JobSources;

public class JobicyJobSource : IJobSource
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<JobicyJobSource> _logger;

    public string SourceName => "Jobicy";
    public string AccessType => "Official Public API";

    public JobicyJobSource(HttpClient httpClient, ILogger<JobicyJobSource> logger)
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
            var url = "https://jobicy.com/api/v2/remote-jobs?count=25&tag=dev";
            _logger.LogInformation("Querying Jobicy API: {Url}", url);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "JYCareerAgent/1.0 (Portfolio Job Search System; contact: jibrankhalily@gmail.com)");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                result.Success = false;
                result.ErrorMessage = $"Jobicy API returned HTTP {response.StatusCode}";
                return result;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("jobs", out var jobsElement))
            {
                foreach (var item in jobsElement.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var pId) ? pId.ToString() : Guid.NewGuid().ToString("N");
                    var title = item.TryGetProperty("jobTitle", out var pTitle) ? pTitle.GetString() ?? "" : "";
                    var company = item.TryGetProperty("companyName", out var pComp) ? pComp.GetString() ?? "" : "";
                    var jobUrl = item.TryGetProperty("url", out var pUrl) ? pUrl.GetString() ?? "" : "";
                    var desc = item.TryGetProperty("jobDescription", out var pDesc) ? pDesc.GetString() ?? "" : "";
                    var location = item.TryGetProperty("jobGeo", out var pLoc) ? pLoc.GetString() ?? "Anywhere" : "Anywhere";

                    DateTime? posted = null;
                    if (item.TryGetProperty("pubDate", out var pPub) && DateTime.TryParse(pPub.GetString(), out var pubDate))
                    {
                        posted = pubDate;
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
                        PostedDate = posted,
                        ApplicationMethod = "Direct"
                    };

                    result.Jobs.Add(job);
                }
            }

            result.Success = true;
            _logger.LogInformation("Jobicy API returned {Count} remote jobs.", result.Jobs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching from Jobicy API");
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
