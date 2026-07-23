// Domain types mirroring CONTRACT.md.

export type Source = 'GooglePlay' | 'Internal';
export type Tag = 'Bug' | 'Feature' | 'Lore' | 'Toxic';
export type Severity = 'Critical' | 'High' | 'Medium' | 'Low' | 'Unknown';
export type Sentiment = 'Positive' | 'Neutral' | 'Negative' | 'Mixed';
export type FeedbackStatus =
  | 'Pending'
  | 'Processing'
  | 'Completed'
  | 'RetryScheduled'
  | 'ManualReview'
  | 'Failed';

export type ImportStatus =
  | 'Queued'
  | 'Fetching'
  | 'Persisting'
  | 'Completed'
  | 'PartiallyCompleted'
  | 'Failed'
  | 'Cancelled';

export type SummaryStatus =
  | 'Pending'
  | 'Generating'
  | 'Ready'
  | 'Invalidated'
  | 'Empty'
  | 'Failed';

export interface LoginResponse {
  token: string;
  expiresAt: string;
}

export interface Game {
  id: string;
  name: string;
  googlePlayUrl: string | null;
  googlePlayPackageId: string | null;
  iconUrl: string | null;
  submissionEnabled: boolean;
  submissionToken: string | null;
  createdAt: string;
}

export interface Paged<T> {
  items: T[];
  nextCursor: string | null;
}

export interface ImportJob {
  id: string;
  gameId: string;
  status: ImportStatus;
  requestedCount: number;
  fetchedCount: number;
  insertedCount: number;
  updatedCount: number;
  skippedCount: number;
  failedCount: number;
  lastErrorCode: string | null;
  lastErrorMessage: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
}

export interface ImportJobCreated {
  id: string;
  status: ImportStatus;
  requestedCount: number;
  createdAt: string;
}

export interface Entity {
  type: string;
  name: string;
  normalizedName: string;
  evidence: string;
  confidence: number;
}

export interface Analysis {
  primaryCategory: string;
  tags: string[];
  severity: Severity;
  toxicity: 'Toxic' | 'NonToxic' | 'Uncertain';
  sentiment: Sentiment;
  summary: string;
  confidence: number;
  requiresManualReview: boolean;
  provider: string;
  model: string;
  createdAt: string;
  entities: Entity[];
}

export interface Feedback {
  id: string;
  gameId: string;
  source: Source;
  externalId: string | null;
  author: string | null;
  text: string;
  rating: number | null;
  appVersion: string | null;
  device: string | null;
  sourceCreatedAt: string | null;
  importedAt: string;
  status: FeedbackStatus;
  attemptCount: number;
  lastErrorCode: string | null;
  lastErrorMessage: string | null;
  analysis: Analysis | null;
}

export interface Dashboard {
  totals: { total: number; bySource: { GooglePlay: number; Internal: number } };
  processing: {
    pending: number;
    processing: number;
    completed: number;
    retryScheduled: number;
    manualReview: number;
    failed: number;
  };
  categories: { Bug: number; Feature: number; Lore: number; Toxic: number; Other: number };
  severities: Record<string, number>;
  sentiments: Record<string, number>;
  criticalBugs: number;
  toxic: number;
  failuresAndManualReview: number;
  topEntities: { normalizedName: string; type: string; count: number }[];
  ratingDistribution: Record<string, number>;
}

export interface Summary {
  id: string;
  status: SummaryStatus;
  overview: string;
  themes: { name: string; count: number }[];
  includedFeedbackCount: number;
  generatedAt: string | null;
  invalidatedAt: string | null;
  provider: string;
  model: string;
}

export interface KeyEntity {
  normalizedName: string;
  type: string;
  mentionCount: number;
  feedbackCount: number;
  sourceBreakdown?: Record<string, number>;
  sentimentBreakdown?: Record<string, number>;
  evidence?: string[];
}

export interface EntitiesResponse {
  items: KeyEntity[];
}

export interface PublicSubmitResponse {
  id: string;
  status: string;
}

export interface PublicStatusResponse {
  id: string;
  status: string;
}

// ----- filter model for the dashboard -----
export type ViewKey = 'critical' | 'feature' | 'lore' | 'toxic' | 'all';
export type SourceTab = 'GooglePlay' | 'Internal' | 'all';

export interface FeedbackFilters {
  source?: Source;
  tag?: Tag;
  severity?: Severity;
  sentiment?: Sentiment;
  status?: FeedbackStatus | 'Active';
  entity?: string;
  search?: string;
  sort?: string;
}

export interface ActivityEvent {
  timestamp: string;
  level: 'run' | 'ok' | 'warn' | 'err' | string;
  gameId?: string | null;
  message: string;
}
