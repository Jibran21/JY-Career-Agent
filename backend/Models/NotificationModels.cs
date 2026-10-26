namespace JYCareerAgent.Api.Models;

public class SystemNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info"; // Info, Success, Warning, Error
    public bool IsRead { get; set; } = false;
    public string? ActionUrl { get; set; }
}
