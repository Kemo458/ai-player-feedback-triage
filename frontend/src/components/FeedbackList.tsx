import { useEffect, useState } from 'react';
import { useFeedbackList } from '../lib/queries';
import { useMarkReviewed } from '../lib/queries';
import type { FeedbackFilters } from '../lib/types';
import { FeedbackRow } from './FeedbackRow';
import { EmptyState, ErrorState, LoadingState } from './ui';
import { useToasts } from './Toasts';

export function FeedbackList({
  gameId,
  filters,
}: {
  gameId: string;
  filters: FeedbackFilters;
}) {
  const query = useFeedbackList(gameId, filters);
  const markReviewed = useMarkReviewed(gameId);
  const { push } = useToasts();
  const [selectedId, setSelectedId] = useState<string>();
  const [expandedId, setExpandedId] = useState<string>();

  const items = query.data?.pages.flatMap((p) => p.items) ?? [];

  useEffect(() => {
    if (items.length === 0) {
      setSelectedId(undefined);
      setExpandedId(undefined);
      return;
    }
    if (!selectedId || !items.some((item) => item.id === selectedId)) {
      setSelectedId(items[0].id);
      setExpandedId(undefined);
    }
  }, [items, selectedId]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      if (
        target?.matches('input, textarea, select, button') ||
        target?.isContentEditable
      ) {
        return;
      }

      const currentIndex = Math.max(
        0,
        items.findIndex((item) => item.id === selectedId),
      );

      if (event.key.toLowerCase() === 'j' || event.key.toLowerCase() === 'k') {
        event.preventDefault();
        const delta = event.key.toLowerCase() === 'j' ? 1 : -1;
        const next = items[Math.max(0, Math.min(items.length - 1, currentIndex + delta))];
        if (!next) return;
        setSelectedId(next.id);
        window.requestAnimationFrame(() => {
          document.getElementById(`feedback-${next.id}`)?.scrollIntoView({
            behavior: 'smooth',
            block: 'center',
          });
        });
      } else if (event.key === 'Enter' && selectedId) {
        event.preventDefault();
        setExpandedId((current) => (current === selectedId ? undefined : selectedId));
      } else if (event.key.toLowerCase() === 'r' && selectedId) {
        const selected = items.find((item) => item.id === selectedId);
        if (selected?.status !== 'ManualReview') return;
        event.preventDefault();
        markReviewed.mutate(selectedId, {
          onSuccess: () => push('Feedback marked as reviewed.', 'success'),
          onError: () => push('Could not mark feedback as reviewed.', 'error'),
        });
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [items, markReviewed, push, selectedId]);

  if (query.isLoading) return <LoadingState label="Loading feedback…" />;
  if (query.isError) return <ErrorState error={query.error} onRetry={() => query.refetch()} />;

  if (items.length === 0) {
    const hasFilters =
      filters.tag ||
      filters.severity ||
      filters.sentiment ||
      filters.status ||
      filters.search ||
      filters.source ||
      filters.entity;
    return (
      <EmptyState
        icon="🔍"
        title="No feedback matches"
        desc={
          hasFilters
            ? 'No items match the current view, source, or filters. Try widening them or clearing the search.'
            : 'No feedback has been imported or submitted for this game yet. Import Google Play reviews or share the public submission link.'
        }
      />
    );
  }

  return (
    <>
      <div className="feed-list">
        {items.map((fb) => (
          <FeedbackRow
            expanded={expandedId === fb.id}
            feedback={fb}
            gameId={gameId}
            key={fb.id}
            onSelect={() => setSelectedId(fb.id)}
            onToggle={() => setExpandedId((current) => (current === fb.id ? undefined : fb.id))}
            selected={selectedId === fb.id}
          />
        ))}
      </div>
      <div className="inline" style={{ justifyContent: 'center', marginTop: 16 }}>
        {query.hasNextPage ? (
          <button
            className="btn"
            onClick={() => query.fetchNextPage()}
            disabled={query.isFetchingNextPage}
          >
            {query.isFetchingNextPage ? 'Loading…' : 'Load more'}
          </button>
        ) : (
          <span className="faint" style={{ fontSize: 12 }}>
            {items.length} item{items.length === 1 ? '' : 's'} · end of list
          </span>
        )}
      </div>
    </>
  );
}
