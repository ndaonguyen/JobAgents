// Mirrors the C# domain records (System.Text.Json web defaults → camelCase).

export interface SearchCriteria {
  roles: string[];
  locations: string[];
  seniority: string;
  mustHaveSkills: string[];
  niceToHaveSkills: string[];
  workStyles: string[];
  salaryExpectation: string | null;
}

export interface JobPosting {
  title: string;
  company: string;
  location: string;
  url: string;
  summary: string;
  postedDate?: string | null;
  description?: string | null;
}

export interface JobMatch {
  posting: JobPosting;
  score: number;
  matchedSkills: string[];
  gaps: string[];
  rationale: string;
}

export interface CompanyInsight {
  company: string;
  summary: string;
  highlights: string[];
  recentNews: string[];
}

export interface SalaryEstimate {
  low: number | null;
  median: number | null;
  high: number | null;
  currency: string;
  basis: string;
}

export interface InterviewPrep {
  likelyQuestions: string[];
  prepNotes: string[];
}

export interface JobDossier {
  match: JobMatch;
  company: CompanyInsight | null;
  salary: SalaryEstimate | null;
  interview: InterviewPrep | null;
}

export interface JobHuntResult {
  criteria: SearchCriteria;
  dossiers: JobDossier[];
  summary: string;
}

// SearchInputs — the persisted/structured form state (mirrors JobAgents.Web.Services.SearchInputs).
export interface SearchInputs {
  roles: string[];
  languages: string[];
  workingStyles: string[];
  location: string;
  salaryMin: number | null;
  salaryMax: number | null;
  currency: string;
  other: string;
  sources: string[];
  minMatchScore: number;
  postedWithin: string | null;
  startDate: string | null;
  endDate: string | null;
  criteria: SearchCriteria | null;
  searchEffort: number;
  researchCompany: boolean;
  researchSalary: boolean;
}

export interface PersistedRun {
  runId: string;
  completedAtUtc: string;
  title: string;
  preferences: string;
  inputs: SearchInputs | null;
  estimatedCostUsd: number | null;
  result: JobHuntResult;
  pinned: boolean;
}

export interface ImprovementIdea {
  id: string;
  title: string;
  description: string;
  status: string;
  createdAtUtc: string;
}

export interface CandidateProfile {
  resumeText: string;
  updatedAtUtc: string;
}

export interface SearchDepthSettings {
  sourcing: boolean;
  sourcingFallback: boolean;
  companyResearch: boolean;
  salaryAnalysis: boolean;
}

export interface AgentModelConfig {
  coordinatorModel: string | null;
  searchModel: string | null;
  resumeMatchModel: string | null;
  companyResearchModel: string | null;
  salaryAnalysisModel: string | null;
  interviewPrepModel: string | null;
  parallelSearch: boolean;
  searchDepth: SearchDepthSettings | null;
  maxResumeChars: number;
  maxDescriptionChars: number;
  maxSearchResultChars: number;
}

export interface ModelOption {
  id: string;
  label: string;
}

export interface JdGap {
  requirement: string;
  severity: string;
  advice: string;
}

export interface JdAnalysis {
  overallScore: number;
  verdict: string;
  matchedStrengths: string[];
  gaps: JdGap[];
  missingKeywords: string[];
  cvSuggestions: string[];
  interviewTalkingPoints: string[];
  summary: string;
}

export type ExpandPiece = 'company' | 'salary' | 'interview';

// One streamed agent event. `kind` is the stable discriminator; the rest of the fields vary by kind.
export interface AgentEvent {
  kind: string;
  runId: { value: string };
  agentId: { value: string };
  timestamp: string;
  // kind-specific (loosely typed; only what the activity log reads):
  role?: string;
  delta?: string;
  finalText?: string;
  estimatedCostUsd?: number | null;
  toolName?: string;
  argumentsJson?: string;
  query?: string;
  isFallback?: boolean;
  postings?: JobPosting[];
  match?: JobMatch;
  insight?: CompanyInsight;
  estimate?: SalaryEstimate;
  prep?: InterviewPrep;
  message?: string;
}
