export interface DashboardStats {
  status: string;
  isRunning: boolean;
  lastRunStartTime: string | null;
  lastRunEndTime: string | null;
  lastRunStatus: string;
  nextScheduledRun: string | null;
  totalJobsInHistory: number;
  totalJobsFoundToday: number;
  highMatchJobsCount: number;
  applicationsSubmittedToday: number;
  maxDailyApplications: number;
  applicationsPendingApproval: number;
  applicationsFailedOrUnconfirmed: number;
  totalApplications: number;
  operatingMode: string;
  latestEmailReport: {
    reportDate: string;
    deliverySuccess: boolean;
    deliveryStatusMessage: string;
    sentAtUtc: string;
  } | null;
  activeSources: { sourceName: string; accessType: string }[];
}

export interface MatchScoreResult {
  totalScore: number;
  dotNetScore: number;
  angularScore: number;
  seniorityScore: number;
  sqlScore: number;
  industryScore: number;
  locationScore: number;
  matchingSkills: string[];
  missingSkills: string[];
  seniorityFit: string;
  locationFit: string;
  reasonsToApply: string[];
  potentialConcerns: string[];
  recommendation: 'HighPriority' | 'ApprovalQueue' | 'Skip';
  summary: string;
}

export interface JobListing {
  id: string;
  platformJobId: string;
  title: string;
  employer: string;
  description: string;
  requiredSkills: string[];
  experienceRequirements: string;
  location: string;
  workArrangement: 'Remote' | 'Hybrid' | 'OnSite' | 'Unspecified';
  salaryRange: string;
  postedDate: string | null;
  sourcePlatform: string;
  jobUrl: string;
  canonicalUrl: string;
  employerCareerPageUrl: string;
  applicationMethod: string;
  isOpen: boolean;
  discoveredAtUtc: string;
  matchScore: MatchScoreResult | null;
  isReviewed: boolean;
  isSkipped: boolean;
  skipReason: string | null;
}

export interface ScreeningQuestionAnswer {
  question: string;
  answer: string;
  requiresApproval: boolean;
  reason: string;
}

export interface AuditEntry {
  timestampUtc: string;
  action: string;
  performedBy: string;
  details: string;
  previousStatus: string;
  newStatus: string;
}

export interface JobApplication {
  id: string;
  jobId: string;
  employer: string;
  positionTitle: string;
  platform: string;
  jobUrl: string;
  matchScore: number;
  cvVersionUsed: string;
  cvFilePath: string;
  coverLetterVersionUsed: string;
  coverLetterFilePath: string;
  generatedCoverLetterText: string;
  submissionMode: 'ApprovalRequired' | 'Assisted' | 'Automatic';
  status: string;
  attemptCount: number;
  applicationStartTimeUtc: string;
  submissionTimestampUtc: string | null;
  timeZone: string;
  confirmationReference: string | null;
  confirmationUrl: string | null;
  errorDetails: string | null;
  pendingReason: string | null;
  nextRecommendedAction: string;
  screeningQuestions: ScreeningQuestionAnswer[];
  auditHistory: AuditEntry[];
}

export interface CandidateProfile {
  fullName: string;
  headline: string;
  location: string;
  email: string;
  phone: string;
  summary: string;
  targetRoles: string[];
  preferredLocations: string[];
  skills: {
    languagesAndFrameworks: string[];
    databaseAndReporting: string[];
    architectureAndMessaging: string[];
    aiAndAgentic: string[];
    toolsAndDevOps: string[];
    mobileDevelopment: string[];
    specialties: string[];
  };
  experience: {
    company: string;
    title: string;
    period: string;
    location: string;
    highlights: string[];
  }[];
  education: {
    degree: string;
    institution: string;
    year: string;
    details: string;
  }[];
  screeningAnswers: {
    workAuthorization: string;
    noticePeriod: string;
    currentLocation: string;
    willingToRelocate: string;
    remotePreference: string;
    targetCompensation: string;
    yearsOfExperience: string;
  };
  masterCvPath: string;
}

export interface AgentSettings {
  operatingMode: 'ApprovalRequired' | 'Assisted' | 'Automatic';
  maxDailyApplications: number;
  matchWeights: {
    dotNetWeight: number;
    angularWeight: number;
    seniorityWeight: number;
    sqlWeight: number;
    industryWeight: number;
    locationWeight: number;
  };
  thresholds: {
    highPriority: number;
    approvalQueue: number;
  };
  searchKeywords: string[];
  searchLocations: string[];
  schedule: {
    scheduledTime: string;
    timeZoneId: string;
    enabled: true;
  };
  email: {
    recipientEmail: string;
    senderEmail: string;
    smtpHost: string;
    smtpPort: number;
    enableSsl: boolean;
    dailyReportEnabled: boolean;
    errorAlertsEnabled: boolean;
    immediateAlertOnSubmission: boolean;
  };
  ai: {
    provider: string;
    model: string;
    temperature: number;
    baseUrl?: string;
  };
  sources: {
    name: string;
    enabled: boolean;
    type: string;
  }[];
  backup: {
    backupBeforeRun: boolean;
    maxBackupsToKeep: number;
  };
  secretStatus?: {
    hasSmtpPassword: boolean;
    hasAiApiKey: boolean;
  };
}

export interface EmailReportRecord {
  id: string;
  reportDate: string;
  runStartTimeUtc: string;
  runEndTimeUtc: string;
  durationFormatted: string;
  subject: string;
  recipientEmail: string;
  deliverySuccess: boolean;
  deliveryStatusMessage: string;
  totalJobsDiscovered: number;
  totalUniqueJobs: number;
  highMatchJobsCount: number;
  applicationsSubmittedCount: number;
  applicationsPendingCount: number;
  applicationsSkippedCount: number;
  duplicatesSkippedCount: number;
  failedSubmissionsCount: number;
  submissionUnconfirmedCount: number;
  htmlBody: string;
  plainTextBody: string;
}

export interface TailoredCvMetadata {
  fileName: string;
  jobId: string;
  employer: string;
  createdAtUtc: string;
  fileSizeBytes: number;
}
