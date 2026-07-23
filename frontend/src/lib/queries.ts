import {
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { api, qs } from './api';
import { queryKeys } from './queryKeys';
import type {
  Dashboard,
  EntitiesResponse,
  Feedback,
  FeedbackFilters,
  Game,
  ImportJob,
  ImportJobCreated,
  ImportStatus,
  Paged,
  PublicStatusResponse,
  Summary,
  SummaryStatus,
} from './types';

const IMPORT_TERMINAL: ImportStatus[] = [
  'Completed',
  'PartiallyCompleted',
  'Failed',
  'Cancelled',
];

// ---------- Games ----------
export function useGames() {
  return useQuery({
    queryKey: queryKeys.games(),
    queryFn: () => api.get<Paged<Game>>(`/api/games${qs({ limit: 100 })}`),
  });
}

export function useGame(gameId: string) {
  return useQuery({
    queryKey: queryKeys.game(gameId),
    queryFn: () => api.get<Game>(`/api/games/${gameId}`),
    enabled: !!gameId,
  });
}

export function useCreateGame() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: { name: string; googlePlayUrl?: string }) =>
      api.post<Game>('/api/games', body),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.games() });
    },
  });
}

export function useDeleteGame() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (gameId: string) => api.del(`/api/games/${gameId}`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.games() });
    },
  });
}

// Retrieve the game's canonical internal-feedback token. This never rotates the link.
export function useGenerateSubmissionToken(gameId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<Game>(`/api/games/${gameId}/submission-token`, {}),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.game(gameId) });
    },
  });
}

// ---------- Queue position (for items awaiting analysis) ----------
export interface QueuePosition {
  status: string;
  aheadCount: number;
  position: number;
  estimatedWaitSeconds: number;
  avgItemSeconds: number;
  globalPending: number;
  globalProcessing: number;
}

export function useQueuePosition(feedbackId: string, enabled: boolean) {
  return useQuery({
    queryKey: ['queue', feedbackId],
    queryFn: () => api.get<QueuePosition>(`/api/feedback/${feedbackId}/queue`),
    enabled,
    refetchInterval: enabled ? 8000 : false,
  });
}

// ---------- Pipeline (hero telemetry) ----------
export interface Pipeline {
  imported: number;
  queued: number;
  analyzing: number;
  done: number;
  failed: number;
  globalQueued: number;
  globalAnalyzing: number;
  throughputPerMin: number;
  avgItemSeconds: number;
  concurrency: number;
  etaSeconds: number;
}

export function usePipeline(gameId: string) {
  return useQuery({
    queryKey: ['pipeline', gameId],
    queryFn: () => api.get<Pipeline>(`/api/games/${gameId}/pipeline`),
    enabled: !!gameId,
    refetchInterval: (query) => {
      const d = query.state.data;
      if (!d) return 5000;
      return d.queued + d.analyzing > 0 ? 4000 : false;
    },
  });
}

// ---------- Dashboard ----------
export function useDashboard(gameId: string) {
  return useQuery({
    queryKey: queryKeys.dashboard(gameId),
    queryFn: () => api.get<Dashboard>(`/api/games/${gameId}/dashboard`),
    enabled: !!gameId,
    // While analysis is still outstanding, poll so the progress banner ticks live even if a
    // SignalR event is missed. Stops once the queue drains.
    refetchInterval: (query) => {
      const p = query.state.data?.processing;
      if (!p) return false;
      return p.pending + p.processing + p.retryScheduled > 0 ? 4000 : false;
    },
  });
}

// ---------- Summary ----------
export function useSummary(
  gameId: string,
  params: { source?: string; tag?: string; severity?: string },
) {
  const cleanParams = {
    source: params.source,
    tag: params.tag,
    severity: params.severity,
  };
  return useQuery({
    queryKey: queryKeys.summary(gameId, cleanParams),
    queryFn: () => api.get<Summary>(`/api/games/${gameId}/summaries${qs(cleanParams)}`),
    enabled: !!gameId,
    // Poll while a summary is generating.
    refetchInterval: (query) => {
      const status = query.state.data?.status as SummaryStatus | undefined;
      return status === 'Pending' || status === 'Generating' ? 2500 : false;
    },
  });
}

export function useRefreshSummary(gameId: string) {
  return useMutation({
    mutationFn: (params: { source?: string; tag?: string; severity?: string }) =>
      api.post<void>(`/api/games/${gameId}/summaries/refresh${qs(params)}`),
  });
}

// ---------- Entities ----------
export function useEntities(gameId: string, params: { type?: string; source?: string }) {
  return useQuery({
    queryKey: queryKeys.entities(gameId, params),
    queryFn: () => api.get<EntitiesResponse>(`/api/games/${gameId}/entities${qs(params)}`),
    enabled: !!gameId,
  });
}

// ---------- Feedback (cursor pagination) ----------
export function useFeedbackList(gameId: string, filters: FeedbackFilters) {
  return useInfiniteQuery({
    queryKey: queryKeys.feedback(gameId, filters),
    initialPageParam: '' as string,
    queryFn: ({ pageParam }) =>
      api.get<Paged<Feedback>>(
        `/api/games/${gameId}/feedback${qs({
          ...filters,
          cursor: pageParam || undefined,
          limit: 50,
        })}`,
      ),
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
    enabled: !!gameId,
  });
}

export function useRetryFeedback(gameId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (feedbackId: string) => api.post<void>(`/api/feedback/${feedbackId}/retry`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['feedback', gameId] });
      qc.invalidateQueries({ queryKey: queryKeys.dashboard(gameId) });
    },
  });
}

export function useMarkReviewed(gameId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (feedbackId: string) =>
      api.post<void>(`/api/feedback/${feedbackId}/mark-reviewed`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['feedback', gameId] });
      qc.invalidateQueries({ queryKey: queryKeys.dashboard(gameId) });
    },
  });
}

// ---------- Imports ----------
export interface ImportRequest {
  url: string;
  count: number;
  language: string;
  country: string;
  sort: 'newest' | 'mostRelevant';
  score: number | null;
}

export function useCreateImport(gameId: string) {
  return useMutation({
    mutationFn: (body: ImportRequest) =>
      api.post<ImportJobCreated>(`/api/games/${gameId}/imports/google-play`, body),
  });
}

export function useImportJob(importId: string | null) {
  return useQuery({
    queryKey: importId ? queryKeys.import(importId) : ['import', 'none'],
    queryFn: () => api.get<ImportJob>(`/api/imports/${importId}`),
    enabled: !!importId,
    // Poll every 2s until terminal (SignalR also nudges this).
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      if (!status) return 2000;
      return IMPORT_TERMINAL.includes(status) ? false : 2000;
    },
  });
}

export function useCancelImport() {
  return useMutation({
    mutationFn: (importId: string) => api.post<void>(`/api/imports/${importId}/cancel`),
  });
}

// ---------- Public submission ----------
export interface PublicSubmitBody {
  text: string;
  rating?: number;
  appVersion?: string;
  device?: string;
  locale?: string;
}

export function usePublicSubmit(gameId: string, token: string) {
  return useMutation({
    mutationFn: (body: PublicSubmitBody) =>
      api.post<{ id: string; status: string }>(
        `/api/public/games/${gameId}/feedback`,
        body,
        { submissionToken: token },
      ),
  });
}

export function usePublicStatus(feedbackId: string | null) {
  return useQuery({
    queryKey: feedbackId ? queryKeys.publicStatus(feedbackId) : ['publicStatus', 'none'],
    queryFn: () =>
      api.get<PublicStatusResponse>(`/api/public/feedback/${feedbackId}/status`, {
        anonymous: true,
      }),
    enabled: !!feedbackId,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      const terminal = status === 'Completed' || status === 'Failed';
      return terminal ? false : 3000;
    },
  });
}

export function isImportTerminal(status: ImportStatus): boolean {
  return IMPORT_TERMINAL.includes(status);
}
