using JYCareerAgent.Api.Models;

namespace JYCareerAgent.Api.Services.JobSources;

public class JobSourceFactory
{
    private readonly IEnumerable<IJobSource> _sources;
    private readonly ILogger<JobSourceFactory> _logger;

    public JobSourceFactory(IEnumerable<IJobSource> sources, ILogger<JobSourceFactory> logger)
    {
        _sources = sources;
        _logger = logger;
    }

    public IEnumerable<IJobSource> GetActiveSources(AgentSettings settings)
    {
        var active = new List<IJobSource>();
        foreach (var src in _sources)
        {
            var config = settings.Sources.FirstOrDefault(s => string.Equals(s.Name, src.SourceName, StringComparison.OrdinalIgnoreCase));
            if (config == null || config.Enabled)
            {
                active.Add(src);
            }
            else
            {
                _logger.LogDebug("Source {SourceName} is disabled in settings.", src.SourceName);
            }
        }
        return active;
    }

    public IEnumerable<IJobSource> GetAllSources() => _sources;
}
