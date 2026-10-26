using System.Text.Json.Serialization;

namespace JYCareerAgent.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OperatingMode
{
    ApprovalRequired,
    Assisted,
    Automatic
}

public class AgentSettings
{
    public OperatingMode OperatingMode { get; set; } = OperatingMode.ApprovalRequired;
    public int MaxDailyApplications { get; set; } = 10;
    public MatchWeights MatchWeights { get; set; } = new();
    public ThresholdSettings Thresholds { get; set; } = new();
    public List<string> SearchKeywords { get; set; } = new();
    public List<string> SearchLocations { get; set; } = new();
    public ScheduleSettings Schedule { get; set; } = new();
    public EmailSettings Email { get; set; } = new();
    public AiSettings Ai { get; set; } = new();
    public List<JobSourceConfig> Sources { get; set; } = new();
    public BackupSettings Backup { get; set; } = new();
}

public class MatchWeights
{
    public double DotNetWeight { get; set; } = 25;
    public double AngularWeight { get; set; } = 20;
    public double SeniorityWeight { get; set; } = 20;
    public double SqlWeight { get; set; } = 15;
    public double IndustryWeight { get; set; } = 10;
    public double LocationWeight { get; set; } = 10;
}

public class ThresholdSettings
{
    public double HighPriority { get; set; } = 80;
    public double ApprovalQueue { get; set; } = 65;
}

public class ScheduleSettings
{
    public string ScheduledTime { get; set; } = "09:00:00";
    public string TimeZoneId { get; set; } = "Asia/Karachi";
    public bool Enabled { get; set; } = true;
}

public class EmailSettings
{
    public string RecipientEmail { get; set; } = "jibrankhalily@gmail.com";
    public string SenderEmail { get; set; } = "jy.career.agent@gmail.com";
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public bool DailyReportEnabled { get; set; } = true;
    public bool ErrorAlertsEnabled { get; set; } = true;
    public bool ImmediateAlertOnSubmission { get; set; } = true;
}

public class AiSettings
{
    public string Provider { get; set; } = "TemplateEngine"; // TemplateEngine, OpenAI, AzureOpenAI, Anthropic, Gemini, Ollama
    public string Model { get; set; } = "gpt-4o-mini";
    public double Temperature { get; set; } = 0.2;
    public string? BaseUrl { get; set; }
}

public class JobSourceConfig
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Type { get; set; } = "Api"; // Api, Feed, ApprovalAssisted
}

public class BackupSettings
{
    public bool BackupBeforeRun { get; set; } = true;
    public int MaxBackupsToKeep { get; set; } = 30;
}
