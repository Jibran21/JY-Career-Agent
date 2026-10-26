import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from './services/api.service';
import {
  DashboardStats,
  JobListing,
  JobApplication,
  CandidateProfile,
  AgentSettings,
  EmailReportRecord,
  TailoredCvMetadata
} from './models/models';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrls: ['./app.css']
})
export class AppComponent implements OnInit, OnDestroy {
  activeTab: 'overview' | 'jobs' | 'approval' | 'applications' | 'cv' | 'reports' | 'settings' = 'overview';

  // State
  stats: DashboardStats | null = null;
  jobs: JobListing[] = [];
  applications: JobApplication[] = [];
  profile: CandidateProfile | null = null;
  settings: AgentSettings | null = null;
  reports: EmailReportRecord[] = [];
  tailoredCvs: TailoredCvMetadata[] = [];
  masterCvText: string = '';

  // Filters & Search
  jobSearchQuery = '';
  jobSourceFilter = '';
  jobMinScoreFilter: number | null = null;
  appStatusFilter = '';
  appSearchQuery = '';

  // Active Modals & Selected items
  selectedApplication: JobApplication | null = null;
  editingCoverLetter = '';
  isEditingCoverLetter = false;
  selectedJobForModal: JobListing | null = null;
  selectedReportForModal: EmailReportRecord | null = null;
  statusModalApp: JobApplication | null = null;
  statusUpdateNewStatus = 'Submitted';
  statusUpdateReason = '';

  // Operation State
  isLoading = false;
  actionMessage = '';
  errorMessage = '';
  testEmailRecipient = '';
  pollInterval: any;

  constructor(public api: ApiService) {}

  ngOnInit(): void {
    this.loadAllData();
    // Auto-refresh stats every 15 seconds
    this.pollInterval = setInterval(() => {
      this.refreshStats();
    }, 15000);
  }

  ngOnDestroy(): void {
    if (this.pollInterval) clearInterval(this.pollInterval);
  }

  loadAllData(): void {
    this.isLoading = true;
    this.refreshStats();
    this.loadJobs();
    this.loadApplications();
    this.loadProfile();
    this.loadSettings();
    this.loadReports();
    this.loadTailoredCvs();
    this.loadMasterCv();
    setTimeout(() => { this.isLoading = false; }, 800);
  }

  refreshStats(): void {
    this.api.getDashboardStats().subscribe({
      next: (data) => { this.stats = data; },
      error: (err) => { console.error('Failed to load stats', err); }
    });
  }

  loadJobs(): void {
    this.api.getJobs({
      query: this.jobSearchQuery,
      source: this.jobSourceFilter,
      minScore: this.jobMinScoreFilter || undefined
    }).subscribe({
      next: (data) => { this.jobs = data; },
      error: (err) => { console.error('Failed to load jobs', err); }
    });
  }

  loadApplications(): void {
    this.api.getApplications(this.appStatusFilter, undefined, this.appSearchQuery).subscribe({
      next: (data) => { this.applications = data; },
      error: (err) => { console.error('Failed to load applications', err); }
    });
  }

  loadProfile(): void {
    this.api.getProfile().subscribe({
      next: (data) => { this.profile = data; },
      error: (err) => { console.error('Failed to load profile', err); }
    });
  }

  loadSettings(): void {
    this.api.getSettings().subscribe({
      next: (data) => {
        this.settings = data;
        if (data.email?.recipientEmail) {
          this.testEmailRecipient = data.email.recipientEmail;
        }
      },
      error: (err) => { console.error('Failed to load settings', err); }
    });
  }

  loadReports(): void {
    this.api.getReports().subscribe({
      next: (data) => { this.reports = data; },
      error: (err) => { console.error('Failed to load reports', err); }
    });
  }

  loadTailoredCvs(): void {
    this.api.getTailoredCvs().subscribe({
      next: (data) => { this.tailoredCvs = data; },
      error: (err) => { console.error('Failed to load tailored CVs', err); }
    });
  }

  loadMasterCv(): void {
    this.api.getMasterCv().subscribe({
      next: (data) => { this.masterCvText = data.content; },
      error: (err) => { console.error('Failed to load master CV', err); }
    });
  }

  // Actions
  triggerRunNow(): void {
    this.actionMessage = 'Triggering execution cycle...';
    this.api.triggerRunNow().subscribe({
      next: (res) => {
        this.actionMessage = res.message;
        setTimeout(() => { this.refreshStats(); this.loadJobs(); this.loadApplications(); }, 2000);
        setTimeout(() => { this.actionMessage = ''; }, 5000);
      },
      error: (err) => {
        this.errorMessage = err.error?.message || 'Failed to start run cycle.';
        setTimeout(() => { this.errorMessage = ''; }, 5000);
      }
    });
  }

  prepareApplication(job: JobListing): void {
    this.actionMessage = `Preparing tailored application for ${job.employer}...`;
    this.api.prepareApplication(job.id).subscribe({
      next: (app) => {
        this.actionMessage = `Application prepared! Available in Approval Queue.`;
        this.loadJobs();
        this.loadApplications();
        this.loadTailoredCvs();
        setTimeout(() => { this.actionMessage = ''; }, 4000);
      },
      error: (err) => {
        this.errorMessage = 'Failed to prepare application.';
        setTimeout(() => { this.errorMessage = ''; }, 4000);
      }
    });
  }

  skipJob(job: JobListing): void {
    const reason = prompt('Reason for skipping this job?', 'Does not align with career goals');
    if (reason === null) return;

    this.api.skipJob(job.id, reason).subscribe({
      next: () => {
        this.loadJobs();
      }
    });
  }

  openApprovalModal(app: JobApplication): void {
    this.selectedApplication = app;
    this.editingCoverLetter = app.generatedCoverLetterText;
    this.isEditingCoverLetter = false;
  }

  closeApprovalModal(): void {
    this.selectedApplication = null;
  }

  saveEditedCoverLetter(): void {
    if (!this.selectedApplication) return;
    this.api.updateApplicationContent(this.selectedApplication.id, this.editingCoverLetter).subscribe({
      next: (updated) => {
        this.selectedApplication = updated;
        this.isEditingCoverLetter = false;
        this.actionMessage = 'Cover letter updated successfully!';
        this.loadApplications();
        setTimeout(() => { this.actionMessage = ''; }, 3000);
      }
    });
  }

  approveAndSubmit(app: JobApplication): void {
    if (!confirm(`Are you sure you want to approve and submit application for ${app.employer} (${app.positionTitle})?`)) return;

    this.actionMessage = `Submitting application to ${app.employer}...`;
    this.api.submitApplication(app.id).subscribe({
      next: (result) => {
        this.actionMessage = `Application status: ${result.status}. Ref: ${result.confirmationReference || 'N/A'}`;
        this.selectedApplication = result;
        this.loadApplications();
        this.refreshStats();
        setTimeout(() => { this.actionMessage = ''; }, 5000);
      },
      error: (err) => {
        this.errorMessage = err.error?.message || 'Submission failed.';
        setTimeout(() => { this.errorMessage = ''; }, 5000);
      }
    });
  }

  openStatusModal(app: JobApplication): void {
    this.statusModalApp = app;
    this.statusUpdateNewStatus = app.status;
    this.statusUpdateReason = '';
  }

  closeStatusModal(): void {
    this.statusModalApp = null;
  }

  saveStatusUpdate(): void {
    if (!this.statusModalApp) return;
    this.api.updateApplicationStatus(this.statusModalApp.id, this.statusUpdateNewStatus, this.statusUpdateReason).subscribe({
      next: () => {
        this.closeStatusModal();
        this.loadApplications();
        this.refreshStats();
        this.actionMessage = 'Application status updated.';
        setTimeout(() => { this.actionMessage = ''; }, 3000);
      }
    });
  }

  saveProfile(): void {
    if (!this.profile) return;
    this.api.updateProfile(this.profile).subscribe({
      next: () => {
        this.actionMessage = 'Profile updated and saved to data/profile.json!';
        setTimeout(() => { this.actionMessage = ''; }, 3000);
      }
    });
  }

  saveSettings(): void {
    if (!this.settings) return;
    this.api.updateSettings(this.settings).subscribe({
      next: () => {
        this.actionMessage = 'Settings updated and saved to data/settings.json!';
        this.refreshStats();
        setTimeout(() => { this.actionMessage = ''; }, 3000);
      }
    });
  }

  sendTestEmail(): void {
    this.actionMessage = 'Sending test email via configured SMTP...';
    this.api.sendTestEmail(this.testEmailRecipient).subscribe({
      next: (res) => {
        if (res.success) {
          this.actionMessage = 'Test email delivered successfully!';
        } else {
          this.errorMessage = `Delivery Notice: ${res.message}`;
        }
        setTimeout(() => { this.actionMessage = ''; this.errorMessage = ''; }, 6000);
      },
      error: (err) => {
        this.errorMessage = 'Failed to send test email.';
        setTimeout(() => { this.errorMessage = ''; }, 5000);
      }
    });
  }

  resendReport(report: EmailReportRecord): void {
    this.actionMessage = `Resending report ${report.reportDate}...`;
    this.api.resendReport(report.id).subscribe({
      next: (res) => {
        this.actionMessage = `Report send result: ${res.message}`;
        this.loadReports();
        setTimeout(() => { this.actionMessage = ''; }, 5000);
      }
    });
  }

  openJobModal(job: JobListing): void {
    this.selectedJobForModal = job;
  }

  closeJobModal(): void {
    this.selectedJobForModal = null;
  }

  openReportModal(report: EmailReportRecord): void {
    this.selectedReportForModal = report;
  }

  closeReportModal(): void {
    this.selectedReportForModal = null;
  }

  getPendingApprovalApps(): JobApplication[] {
    return this.applications.filter(a => a.status === 'PendingApproval' || a.status === 'ReadyToSubmit');
  }

  formatDate(utcDate: string | null | undefined): string {
    if (!utcDate) return 'N/A';
    try {
      const d = new Date(utcDate);
      return d.toLocaleString('en-US', { timeZone: 'Asia/Karachi' }) + ' PKT';
    } catch {
      return utcDate;
    }
  }

  getDownloadUrl(fileName: string): string {
    return this.api.getDownloadTailoredCvUrl(fileName);
  }
}

export { AppComponent as App };
