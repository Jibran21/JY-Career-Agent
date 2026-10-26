import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  DashboardStats,
  JobListing,
  JobApplication,
  CandidateProfile,
  AgentSettings,
  EmailReportRecord,
  TailoredCvMetadata
} from '../models/models';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private readonly baseUrl = 'http://localhost:5000/api';

  constructor(private http: HttpClient) {}

  // Dashboard
  getDashboardStats(): Observable<DashboardStats> {
    return this.http.get<DashboardStats>(`${this.baseUrl}/dashboard/stats`);
  }

  triggerRunNow(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.baseUrl}/dashboard/run-now`, {});
  }

  // Jobs
  getJobs(params?: { query?: string; source?: string; minScore?: number; onlyUnreviewed?: boolean }): Observable<JobListing[]> {
    let url = `${this.baseUrl}/jobs?`;
    if (params?.query) url += `query=${encodeURIComponent(params.query)}&`;
    if (params?.source) url += `source=${encodeURIComponent(params.source)}&`;
    if (params?.minScore !== undefined) url += `minScore=${params.minScore}&`;
    if (params?.onlyUnreviewed !== undefined) url += `onlyUnreviewed=${params.onlyUnreviewed}&`;
    return this.http.get<JobListing[]>(url);
  }

  prepareApplication(jobId: string): Observable<JobApplication> {
    return this.http.post<JobApplication>(`${this.baseUrl}/jobs/${jobId}/prepare`, {});
  }

  skipJob(jobId: string, reason: string): Observable<JobListing> {
    return this.http.post<JobListing>(`${this.baseUrl}/jobs/${jobId}/skip`, { reason });
  }

  // Applications
  getApplications(status?: string, platform?: string, search?: string): Observable<JobApplication[]> {
    let url = `${this.baseUrl}/applications?`;
    if (status) url += `status=${status}&`;
    if (platform) url += `platform=${encodeURIComponent(platform)}&`;
    if (search) url += `search=${encodeURIComponent(search)}&`;
    return this.http.get<JobApplication[]>(url);
  }

  getApplicationById(id: string): Observable<JobApplication> {
    return this.http.get<JobApplication>(`${this.baseUrl}/applications/${id}`);
  }

  submitApplication(id: string): Observable<JobApplication> {
    return this.http.post<JobApplication>(`${this.baseUrl}/applications/${id}/submit`, {});
  }

  updateApplicationStatus(id: string, status: string, reason: string): Observable<JobApplication> {
    return this.http.put<JobApplication>(`${this.baseUrl}/applications/${id}/status`, { status, reason });
  }

  updateApplicationContent(id: string, coverLetterText: string): Observable<JobApplication> {
    return this.http.put<JobApplication>(`${this.baseUrl}/applications/${id}/content`, { coverLetterText });
  }

  // CV & Profile
  getProfile(): Observable<CandidateProfile> {
    return this.http.get<CandidateProfile>(`${this.baseUrl}/cv/profile`);
  }

  updateProfile(profile: CandidateProfile): Observable<CandidateProfile> {
    return this.http.put<CandidateProfile>(`${this.baseUrl}/cv/profile`, profile);
  }

  getMasterCv(): Observable<{ content: string }> {
    return this.http.get<{ content: string }>(`${this.baseUrl}/cv/master`);
  }

  getTailoredCvs(): Observable<TailoredCvMetadata[]> {
    return this.http.get<TailoredCvMetadata[]>(`${this.baseUrl}/cv/tailored`);
  }

  getDownloadTailoredCvUrl(fileName: string): string {
    return `${this.baseUrl}/cv/tailored/${encodeURIComponent(fileName)}/download`;
  }

  // Settings
  getSettings(): Observable<AgentSettings> {
    return this.http.get<AgentSettings>(`${this.baseUrl}/settings`);
  }

  updateSettings(settings: AgentSettings): Observable<AgentSettings> {
    return this.http.put<AgentSettings>(`${this.baseUrl}/settings`, settings);
  }

  sendTestEmail(recipientEmail?: string): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>(`${this.baseUrl}/settings/email/test`, { recipientEmail });
  }

  // Reports
  getReports(): Observable<EmailReportRecord[]> {
    return this.http.get<EmailReportRecord[]>(`${this.baseUrl}/reports`);
  }

  resendReport(id: string): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>(`${this.baseUrl}/reports/${id}/resend`, {});
  }
}
