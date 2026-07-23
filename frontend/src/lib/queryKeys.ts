import type { FeedbackFilters } from './types';

// Centralised react-query keys so SignalR invalidation can target them.
export const queryKeys = {
  games: () => ['games'] as const,
  game: (gameId: string) => ['game', gameId] as const,
  dashboard: (gameId: string) => ['dashboard', gameId] as const,
  summary: (gameId: string, params: Record<string, string | undefined>) =>
    ['summary', gameId, params] as const,
  feedback: (gameId: string, filters: FeedbackFilters) => ['feedback', gameId, filters] as const,
  entities: (gameId: string, params: Record<string, string | undefined>) =>
    ['entities', gameId, params] as const,
  import: (importId: string) => ['import', importId] as const,
  publicStatus: (feedbackId: string) => ['publicStatus', feedbackId] as const,
};

// Prefixes used for broad invalidation on a per-game basis.
export function gameScopedPrefixes(gameId: string) {
  return [
    ['dashboard', gameId],
    ['summary', gameId],
    ['feedback', gameId],
    ['entities', gameId],
  ];
}
