namespace JYCareerAgent.Api.Models;

public class CandidateProfile
{
    public string FullName { get; set; } = "Jibran Yousuf Khan";
    public string Headline { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> TargetRoles { get; set; } = new();
    public List<string> PreferredLocations { get; set; } = new();
    public SkillCatalog Skills { get; set; } = new();
    public List<WorkExperience> Experience { get; set; } = new();
    public List<EducationEntry> Education { get; set; } = new();
    public ScreeningAnswers ScreeningAnswers { get; set; } = new();
    public string MasterCvPath { get; set; } = "cv/master-cv/Jibran_Yousuf_Khan_CV.txt";
}

public class SkillCatalog
{
    public List<string> LanguagesAndFrameworks { get; set; } = new();
    public List<string> DatabaseAndReporting { get; set; } = new();
    public List<string> ArchitectureAndMessaging { get; set; } = new();
    public List<string> AiAndAgentic { get; set; } = new();
    public List<string> ToolsAndDevOps { get; set; } = new();
    public List<string> MobileDevelopment { get; set; } = new();
    public List<string> Specialties { get; set; } = new();

    public IEnumerable<string> AllSkills => LanguagesAndFrameworks
        .Concat(DatabaseAndReporting)
        .Concat(ArchitectureAndMessaging)
        .Concat(AiAndAgentic)
        .Concat(ToolsAndDevOps)
        .Concat(MobileDevelopment)
        .Concat(Specialties)
        .Distinct(StringComparer.OrdinalIgnoreCase);
}

public class WorkExperience
{
    public string Company { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = new();
}

public class EducationEntry
{
    public string Degree { get; set; } = string.Empty;
    public string Institution { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public class ScreeningAnswers
{
    public string WorkAuthorization { get; set; } = string.Empty;
    public string NoticePeriod { get; set; } = string.Empty;
    public string CurrentLocation { get; set; } = string.Empty;
    public string WillingToRelocate { get; set; } = string.Empty;
    public string RemotePreference { get; set; } = string.Empty;
    public string TargetCompensation { get; set; } = string.Empty;
    public string YearsOfExperience { get; set; } = string.Empty;
}
