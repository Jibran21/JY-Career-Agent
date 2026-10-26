using System.Text.RegularExpressions;
using JYCareerAgent.Api.Models;

namespace JYCareerAgent.Api.Services.Matching;

public class JobNormalizationService : IJobNormalizationService
{
    private static readonly HashSet<string> TrackingQueryParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "ref", "source", "gh_src", "fbclid", "gclid", "trackingid", "trk", "midToken"
    };

    public string CanonicalizeUrl(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return string.Empty;

        try
        {
            if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
                return rawUrl.Trim();

            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var keysToRemove = query.AllKeys
                .Where(k => k != null && TrackingQueryParams.Contains(k))
                .ToList();

            foreach (var key in keysToRemove)
            {
                query.Remove(key);
            }

            var builder = new UriBuilder(uri)
            {
                Query = query.ToString(),
                Fragment = string.Empty
            };

            var cleanUrl = builder.Uri.ToString();
            // Remove trailing slash if path is not root
            return cleanUrl.TrimEnd('/');
        }
        catch
        {
            return rawUrl.Trim();
        }
    }

    public string CreateJobFingerprint(string employer, string title, string location)
    {
        var normEmployer = CleanString(employer);
        var normTitle = CleanString(title);
        var normLocation = CleanString(location);

        return $"{normEmployer}|{normTitle}|{normLocation}";
    }

    public bool IsDuplicate(JobListing candidate, IEnumerable<JobListing> existingJobs)
    {
        var canonicalUrl = CanonicalizeUrl(candidate.JobUrl);
        var fingerprint = CreateJobFingerprint(candidate.Employer, candidate.Title, candidate.Location);

        foreach (var existing in existingJobs)
        {
            // 1. Exact platform ID match within same platform
            if (!string.IsNullOrWhiteSpace(candidate.PlatformJobId) &&
                !string.IsNullOrWhiteSpace(existing.PlatformJobId) &&
                string.Equals(candidate.SourcePlatform, existing.SourcePlatform, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.PlatformJobId, existing.PlatformJobId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 2. Exact canonical URL match
            if (!string.IsNullOrWhiteSpace(canonicalUrl) &&
                !string.IsNullOrWhiteSpace(existing.CanonicalUrl) &&
                string.Equals(canonicalUrl, existing.CanonicalUrl, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 3. Normalized fingerprint match (Employer + Title + Location)
            var existingFingerprint = CreateJobFingerprint(existing.Employer, existing.Title, existing.Location);
            if (string.Equals(fingerprint, existingFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public void NormalizeJob(JobListing job)
    {
        job.Title = job.Title?.Trim() ?? string.Empty;
        job.Employer = job.Employer?.Trim() ?? string.Empty;
        job.Location = job.Location?.Trim() ?? string.Empty;
        job.CanonicalUrl = CanonicalizeUrl(job.JobUrl);

        // Normalize work arrangement
        var textToScan = $"{job.Title} {job.Location} {job.Description}";
        if (textToScan.Contains("remote", StringComparison.OrdinalIgnoreCase) || 
            textToScan.Contains("work from home", StringComparison.OrdinalIgnoreCase) ||
            textToScan.Contains("anywhere", StringComparison.OrdinalIgnoreCase))
        {
            job.WorkArrangement = WorkArrangement.Remote;
        }
        else if (textToScan.Contains("hybrid", StringComparison.OrdinalIgnoreCase))
        {
            job.WorkArrangement = WorkArrangement.Hybrid;
        }
        else if (textToScan.Contains("on-site", StringComparison.OrdinalIgnoreCase) || 
                 textToScan.Contains("onsite", StringComparison.OrdinalIgnoreCase) ||
                 textToScan.Contains("in office", StringComparison.OrdinalIgnoreCase))
        {
            job.WorkArrangement = WorkArrangement.OnSite;
        }

        // Clean up title formatting if contains redundant prefixes
        job.Title = Regex.Replace(job.Title, @"\s+", " ");
        job.Employer = Regex.Replace(job.Employer, @"\s+", " ");
    }

    private static string CleanString(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var lower = input.Trim().ToLowerInvariant();
        return Regex.Replace(lower, @"[^a-z0-9]", "");
    }
}
