# JY Career Agent — Agentic AI Job Search and Application Automation System

**JY Career Agent** is a production-oriented, agentic software engineering career automation platform architected specifically for **Jibran Yousuf Khan** (~10 years experience; Senior .NET, Angular, SQL Server/SSIS, and AI Agent Engineer).

The system autonomously queries permitted job feeds and APIs, evaluates and scores opportunities with transparent 0–100 matching weights against Jibran's verified master CV, synthesizes grounded ATS-tailored CVs and cover letters without hallucinations, tracks application lifecycles with strict audit trails, and automatically emails a detailed daily summary report every morning.

---

## 1. Key Architectural Highlights & Strict Guarantees

* **Strict No-Database Architecture**: Zero databases (no SQL Server, MySQL, SQLite, MongoDB, PostgreSQL, Redis, or EF Core). All persistent data is stored safely in structured JSON files in `data/`.
* **Atomic Replacement & Concurrency Protection**: High-reliability `JsonStorageService` employs asynchronous thread synchronization (`ConcurrentDictionary<string, SemaphoreSlim>`), atomic file replacement (`.tmp` flush followed by atomic rename), and automatic timestamped file backups in `backups/`.
* **Zero Hallucination / Ground Truth**: The candidate's real master CV (`cv/master-cv/Jibran_Yousuf_Khan_CV.txt`) and verified profile (`data/profile.json`) serve as ground truth. No skills, certifications, degrees, or employers are fabricated.
* **Operating Modes & Opt-In Automation**: Starts strictly in **Mode A (Approval Required)**. Applications are prepared and queued for candidate review in the Angular dashboard. Automatic submission (Mode C) is an explicit opt-in setting with daily limits (default 10) and checks against platform terms.
* **Asia/Karachi Timezone & Accurate Status Tracking**: All scheduled executions, submission logs, and daily email summaries operate on `Asia/Karachi` time.
* **Daily Scheduler & Recovery**: Background service runs every morning at **9:00 AM PKT**. Progress is recorded to `data/agent-state.json` allowing resumption after restarts.
* **Automated Daily Email Summary**: Rich HTML and plain text daily report generated at the end of every run, emailed via MailKit SMTP, and logged to `data/email-reports.json`.

---

## 2. Directory Structure

```text
JY-Career-Agent/
├── .env.example                     # Environment secrets template (SMTP, AI keys)
├── README.md                        # Complete setup, architecture & operational guide
├── JYCareerAgent.sln                # .NET solution file
├── backend/                         # ASP.NET Core Web API (.NET 10.0 LTS)
│   ├── Controllers/
│   │   ├── DashboardController.cs   # Metrics, system status, "Run Now" trigger
│   │   ├── JobsController.cs        # Job listing, filtering, application prep
│   │   ├── ApplicationsController.cs# Status workflow, approvals, document editing
│   │   ├── CvController.cs          # Profile management, master CV, tailored downloads
│   │   ├── SettingsController.cs    # Configuration & non-secret status
│   │   └── ReportsController.cs     # Daily report history & manual resend
│   ├── Models/                      # Domain models (Profile, Job, Application, Settings)
│   ├── Services/
│   │   ├── Storage/                 # JsonStorageService (atomic writes, concurrency, backups)
│   │   ├── CV/                      # CvManagerService (ATS tailoring, master CV loading)
│   │   ├── Matching/                # JobMatchingEngine (0-100 score), JobNormalizationService
│   │   ├── JobSources/              # Remotive, Arbeitnow, Jobicy, Rozee, LinkedIn, Indeed
│   │   ├── AI/                      # AiAgentService (Cover letters, prompts, LLM client)
│   │   ├── Applications/            # ApplicationWorkflowService (Limits, Mode A/B/C)
│   │   ├── Email/                   # EmailReportingService (HTML daily report, MailKit)
│   │   └── Scheduling/              # JobSchedulerBackgroundService (9 AM PKT), Orchestrator
│   ├── Program.cs                   # DI configuration, CORS, HTTP clients
│   └── JYCareerAgent.Api.csproj
├── frontend/                        # Angular 22 Dashboard (TypeScript & Vanilla CSS)
│   ├── src/
│   │   ├── app/
│   │   │   ├── models/              # TypeScript interfaces matching backend
│   │   │   ├── services/            # ApiService (strongly-typed HTTP client)
│   │   │   ├── app.ts               # Standalone component with full UI state
│   │   │   ├── app.html             # Multi-tab responsive dashboard
│   │   │   └── app.css              # Custom dark-slate & indigo design system
│   │   ├── styles.css               # Global tokens, typography, utilities
│   │   └── main.ts
│   ├── angular.json
│   └── package.json
├── tests/                           # xUnit Automated Test Suite (.NET 10.0)
│   └── JYCareerAgent.Tests/
│       ├── JsonStorageTests.cs      # Atomic writes, concurrency, backup, recovery
│       ├── JobDeduplicationTests.cs # Canonical URLs, platform ID & fingerprint matching
│       ├── JobMatchingTests.cs      # 0-100 scoring engine with custom weights
│       ├── CvTailoringValidationTests.cs # Real employer & degree preservation
│       ├── ApplicationWorkflowTests.cs   # Mode A/B/C, daily limits, Karachi timestamps
│       ├── EmailReportTests.cs      # Report generation, secret redaction, SMTP failure
│       └── FullSimulationTests.cs   # End-to-end full daily cycle simulation
├── data/                            # Local persistent JSON datastore (NO Database)
│   ├── profile.json                 # Candidate verified profile & screening answers
│   ├── settings.json                # Operating parameters, weights, sources, schedule
│   ├── job-history.json             # Discovered & evaluated jobs
│   ├── applications.json            # Application lifecycle records & audit histories
│   ├── agent-state.json             # Scheduler & execution state
│   ├── email-reports.json           # Past daily execution reports
│   └── notifications.json           # System alerts & notices
├── cv/
│   ├── master-cv/                   # Approved master CV text source
│   └── tailored/                    # ATS-tailored CVs generated for individual jobs
├── cover-letters/                   # Grounded, tailored cover letters
├── prompts/                         # AI prompts (job_matching, cv_tailoring, cover_letter)
├── backups/                         # Timestamped JSON backups before mutations
└── logs/                            # Application logs
```

---

## 3. Technology Stack & Verification

| Layer | Technology | Details / Version |
| :--- | :--- | :--- |
| **Backend** | C# / ASP.NET Core Web API | **.NET 10.0.402** (Modern LTS framework, DI, BackgroundService, HttpClientFactory, OpenAPI) |
| **Storage** | **Dedicated JSON Storage Engine** | Strictly zero database packages. Concurrency locks, atomic file replacement, backups |
| **Email** | **MailKit 4.18.2** | Secure SMTP over TLS, HTML + plain text templating, retry handling |
| **Testing** | **xUnit 2.9.3 + FluentAssertions** | 24 automated unit and integration simulation tests (**0 failures**) |
| **Frontend** | **Angular 22 / TypeScript** | Standalone components, Reactive forms, responsive dashboard, zero external CSS bloat |
| **AI Integration** | **Configurable LLM & Template Engine** | Offline deterministic engine out-of-the-box; supports OpenAI, Azure, Gemini, Anthropic, Ollama |

---

## 4. Supported Job Sources & Access Compliance

In strict adherence to platform terms and anti-bot regulations:

1. **Remotive**: Official Public Developer API (`https://remotive.com/api/remote-jobs`) for global remote software engineering roles.
2. **Arbeitnow**: Official Public Jobs API (`https://www.arbeitnow.com/api/job-board-api`) for tech and remote jobs.
3. **Jobicy**: Official Remote Jobs API (`https://jobicy.com/api/v2/remote-jobs`).
4. **Rozee.pk Adapter**: Curated Pakistani enterprise feed covering Karachi, Islamabad, Lahore, and remote .NET/Angular openings.
5. **LinkedIn (Approval-Assisted)**: Curated discovery with direct career links. Does not bypass CAPTCHAs, bot shields, or platform login barriers; routes to candidate approval queue for verified submission.
6. **Indeed (Approval-Assisted)**: Curated opening analysis with compliance-grounded assisted review.

---

## 5. Job Matching & Scoring Engine

Each job is evaluated transparently from **0 to 100** based on configured weights:

* **.NET / C# Relevance (25%)**: C#, .NET Core, ASP.NET Core, Entity Framework Core, Web API, Clean Architecture.
* **Angular / Frontend Relevance (20%)**: Angular (v14+), TypeScript, RxJS, responsive SPA development.
* **Seniority & Experience Fit (20%)**: Matched against Jibran's ~10 years tenure (Senior, Lead, Architect).
* **SQL / Data Engineering Relevance (15%)**: MS SQL Server, SSIS ETL pipelines, Power BI, T-SQL performance tuning.
* **Industry / Domain Relevance (10%)**: FinTech, Digital Banking, Healthcare ERP, Supply Chain SaaS.
* **Location & Work Arrangement (10%)**: Karachi on-site/hybrid (100%), Pakistan remote (100%), International remote (95%), Relocation (80%).

### Decision Rules:
* **80 – 100**: **High Priority** — Top application candidate; automatically queued for application preparation.
* **65 – 79**: **Approval Queue** — Reviewed and prepared for candidate verification.
* **< 65**: **Skip** — Kept in history to avoid redundant processing.

---

## 6. How to Run the Application Locally

### Prerequisites
1. **.NET 10 SDK** (Installed: `10.0.402`)
2. **Node.js** (Installed: `v24.21.0`) & npm
3. **Angular CLI** (Installed: `22.1.8`)

### Step 1: Configure Environment Variables
Copy `.env.example` to `.env` in the root folder:
```bash
cp .env.example .env
```
Open `.env` and set your credentials:
```env
# Email SMTP Credentials (e.g. Gmail App Password)
JY_EMAIL__SMTP_PASSWORD=your_gmail_app_password
JY_EMAIL__SMTP_USERNAME=jibrankhalily@gmail.com
JY_EMAIL__SMTP_HOST=smtp.gmail.com
JY_EMAIL__SMTP_PORT=587

# AI Provider (Optional: defaults to built-in offline TemplateEngine)
JY_AI__PROVIDER=TemplateEngine
JY_AI__API_KEY=your_openai_or_gemini_key
```

### Step 2: Start the Backend API
```powershell
cd f:\Jibran\JY-Career-Agent\backend
dotnet run
```
* Backend API starts on: **`http://localhost:5000`**
* OpenAPI Documentation: **`http://localhost:5000/openapi/v1.json`**

### Step 3: Start the Angular Dashboard
In a separate terminal:
```powershell
cd f:\Jibran\JY-Career-Agent\frontend
npm start
```
* Angular Dashboard opens on: **`http://localhost:4200`**

---

## 7. Configuring the 9:00 AM Daily Run

### Application-Level Scheduler
The built-in `JobSchedulerBackgroundService` runs continuously in the background and computes the exact delay to **9:00:00 AM Asia/Karachi time** each day using named timezone mapping (`Asia/Karachi` / `Pakistan Standard Time`).

You can customize the daily execution time anytime in `data/settings.json` or directly from the **Settings** tab in the Angular Dashboard.

### Windows Task Scheduler (Automatic Startup on Boot)
To ensure the agent launches automatically whenever Windows boots:

1. Open **Windows Task Scheduler** (`taskschd.msc`).
2. Click **Create Task** (name it `JY Career Agent Host`).
3. Under **General**, select **Run whether user is logged on or not** and **Run with highest privileges**.
4. In **Triggers**, add:
   * **At startup** (with a 1-minute delay).
   * Or **Daily at 8:55 AM**.
5. In **Actions**, choose **Start a program**:
   * **Program/script**: `dotnet`
   * **Add arguments**: `run --project f:\Jibran\JY-Career-Agent\backend\JYCareerAgent.Api.csproj`
   * **Start in**: `f:\Jibran\JY-Career-Agent\backend`
6. In **Settings**, check **If the task fails, restart every: 5 minutes**.

---

## 8. Testing & Validation

Run the complete automated test suite (24 tests covering storage, deduplication, matching, tailoring, workflow, email, and full end-to-end simulation):

```powershell
cd f:\Jibran\JY-Career-Agent
dotnet test tests\JYCareerAgent.Tests\JYCareerAgent.Tests.csproj
```

**Test Results:**
```text
Passed!  - Failed: 0, Passed: 24, Skipped: 0, Total: 24, Duration: 2 s
```

---

## 9. Security, Privacy & Secret Redaction

* **No Secrets in Source Control or JSON**: `profile.json`, `settings.json`, and `email-reports.json` never contain passwords, tokens, or private keys.
* **Environment-Based Secret Injection**: Secrets are read exclusively from environment variables (`JY_EMAIL__SMTP_PASSWORD`, `JY_AI__API_KEY`).
* **Input Sanitization**: External job descriptions and web feeds are stripped of HTML and filtered against prompt-injection delimiters before AI processing.
* **Local Data Sovereignty**: All CV files, generated cover letters, and application logs remain strictly on local disk.

---

## 10. Backup & Disaster Recovery

* **Automatic Backups**: Created in `backups/` before significant state changes.
* **Manual Restoration**:
  ```csharp
  // To restore any JSON file from a timestamped backup:
  await storageService.RestoreBackupAsync("data/job-history.json", "job-history_20261023_190000_123.json");
  ```
* Historical backups are automatically rotated to preserve the latest 30 snapshots per file.
