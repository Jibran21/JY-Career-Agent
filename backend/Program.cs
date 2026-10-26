using System.Text.Json;
using System.Text.Json.Serialization;
using JYCareerAgent.Api.Services.AI;
using JYCareerAgent.Api.Services.Applications;
using JYCareerAgent.Api.Services.CV;
using JYCareerAgent.Api.Services.Email;
using JYCareerAgent.Api.Services.JobSources;
using JYCareerAgent.Api.Services.Matching;
using JYCareerAgent.Api.Services.Scheduling;
using JYCareerAgent.Api.Services.Storage;

var builder = WebApplication.CreateBuilder(args);

// Load environment variables from .env file if it exists
var potentialEnvPaths = new[]
{
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    Path.Combine(Directory.GetCurrentDirectory(), "..", ".env"),
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env")
};

foreach (var envPath in potentialEnvPaths)
{
    if (File.Exists(envPath))
    {
        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;
            var parts = trimmed.Split('=', 2);
            if (parts.Length == 2)
            {
                Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
            }
        }
        break;
    }
}

// 1. JSON & Controller Setup
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// 2. CORS Policy for Angular Frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "http://127.0.0.1:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 3. HTTP Clients for Sources & AI
builder.Services.AddHttpClient<RemotiveJobSource>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<ArbeitnowJobSource>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<JobicyJobSource>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<RozeeJobSource>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<AiAgentService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
});

// 4. Core Storage & Domain Services (Strictly NO Database)
builder.Services.AddSingleton<IJsonStorageService, JsonStorageService>();
builder.Services.AddSingleton<IJobNormalizationService, JobNormalizationService>();
builder.Services.AddSingleton<IJobMatchingEngine, JobMatchingEngine>();

builder.Services.AddScoped<ICvManagerService, CvManagerService>();
builder.Services.AddScoped<IAiAgentService, AiAgentService>();
builder.Services.AddScoped<IApplicationWorkflowService, ApplicationWorkflowService>();
builder.Services.AddScoped<IEmailReportingService, EmailReportingService>();
builder.Services.AddScoped<AgentOrchestrator>();

// 5. Job Source Registrations
builder.Services.AddSingleton<IJobSource, RemotiveJobSource>();
builder.Services.AddSingleton<IJobSource, ArbeitnowJobSource>();
builder.Services.AddSingleton<IJobSource, JobicyJobSource>();
builder.Services.AddSingleton<IJobSource, RozeeJobSource>();
builder.Services.AddSingleton<IJobSource, LinkedInJobSource>();
builder.Services.AddSingleton<IJobSource, IndeedJobSource>();
builder.Services.AddSingleton<JobSourceFactory>();

// 6. Scheduler Background Service
builder.Services.AddHostedService<JobSchedulerBackgroundService>();

// 7. OpenAPI / Swagger Documentation
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.UseCors("AllowAngularDev");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => Results.Ok(new
{
    Service = "JY Career Agent API",
    Version = "1.0.0",
    Status = "Healthy",
    Mode = "Strict JSON Storage (No Database)",
    Candidate = "Jibran Yousuf Khan",
    TimestampUtc = DateTime.UtcNow
}));

app.Run();
