import { useSummary, useRefreshSummary } from '../lib/queries';
import { ErrorState, LoadingState, formatDate } from './ui';
import { useToasts } from './Toasts';

interface Props {
  gameId: string;
  params: { source?: string; tag?: string; severity?: string };
  onThemeSelect?: (theme: string) => void;
}

export function SummaryPanel({ gameId, params, onThemeSelect }: Props) {
  const query = useSummary(gameId, params);
  const refresh = useRefreshSummary(gameId);
  const { push } = useToasts();

  const onRefresh = async () => {
    try {
      await refresh.mutateAsync(params);
      push('AI summary regeneration started.', 'info');
      // Optimistically re-poll.
      setTimeout(() => query.refetch(), 500);
    } catch {
      push('Could not refresh the AI summary.', 'error');
    }
  };

  const summary = query.data;
  const generating = summary?.status === 'Generating' || summary?.status === 'Pending';
  const hasCachedSummary =
    !!summary &&
    summary.includedFeedbackCount > 0 &&
    !!summary.overview &&
    !summary.overview.startsWith('Summary generation failed');
  const isFallback = summary?.provider === 'FastFallback';
  const providerLabel = isFallback
    ? 'Fast fallback · Qwen busy'
    : summary?.provider
      ? `${summary.provider}${summary.model ? ` · ${summary.model}` : ''}`
      : '';

  return (
    <section className="panel">
      <div className="panel-head">
        <div>
          <h3>AI Summary</h3>
          <span className="sub">
            {summary?.status === 'Failed' && hasCachedSummary
              ? 'Refresh failed — showing the previous summary'
              : summary?.status === 'Failed'
                ? 'Generation failed — retry when ready'
                : generating && hasCachedSummary
                  ? 'Refreshing with Qwen — previous summary remains visible'
              : summary?.status === 'Invalidated'
                ? 'Stale — regenerate for fresh insights'
                : summary?.generatedAt
                  ? `Generated ${formatDate(summary.generatedAt)}`
                  : 'Aggregate view of current filter'}
          </span>
        </div>
        <div className="summary-head-actions">
          {providerLabel && <span className={`summary-provider ${isFallback ? 'fallback' : ''}`}>{providerLabel}</span>}
          <button
            className="btn sm"
            onClick={onRefresh}
            disabled={refresh.isPending || generating}
          >
            {refresh.isPending || generating
              ? hasCachedSummary
                ? 'Refreshing…'
                : 'Queued…'
              : isFallback
                ? 'Try Qwen refresh'
                : 'Refresh'}
          </button>
        </div>
      </div>

      <div className="panel-pad">
        {query.isLoading ? (
          <LoadingState label="Loading summary…" />
        ) : query.isError ? (
          <ErrorState error={query.error} onRetry={() => query.refetch()} />
        ) : !summary ? (
          <div className="summary-empty">No summary yet.</div>
        ) : summary.status === 'Empty' ? (
          <div className="summary-empty">
            Not enough feedback matches this filter to generate a summary yet.
          </div>
        ) : generating && !hasCachedSummary ? (
          <div className="summary-generating">
            <span className="spinner" />
            <div>
              <strong>Summary queued</strong>
              <span>
                Qwen will summarize this view when review-analysis capacity is available.
              </span>
            </div>
          </div>
        ) : summary.status === 'Failed' && !hasCachedSummary ? (
          <div className="error-box">Summary generation failed. Click Refresh to retry.</div>
        ) : (
          <>
            {generating && (
              <div className="summary-refreshing">
                <span className="spinner" />
                Refreshing in the background. The last complete summary stays available.
              </div>
            )}
            {summary.status === 'Failed' && (
              <div className="error-box" style={{ marginBottom: 12 }}>
                The refresh did not finish. The previous complete summary is shown below.
              </div>
            )}
            {summary.status === 'Invalidated' && (
              <div className="error-box" style={{ marginBottom: 12 }}>
                This summary is stale (invalidated{' '}
                {formatDate(summary.invalidatedAt)}). Click Refresh to regenerate.
              </div>
            )}
            <p className="summary-overview">{summary.overview || 'No overview available.'}</p>
            {summary.themes?.length > 0 && (
              <div className="chip-row mt">
                {summary.themes.map((t) => (
                  <button
                    className="chip accent chip-action"
                    key={t.name}
                    onClick={() => onThemeSelect?.(t.name)}
                    title="Search feedback for this theme"
                    type="button"
                  >
                    {t.name}
                    <span className="faint">· {t.count}</span>
                  </button>
                ))}
              </div>
            )}
            <div className="faint mt" style={{ fontSize: 12 }}>
              Based on {summary.includedFeedbackCount} feedback item
              {summary.includedFeedbackCount === 1 ? '' : 's'}.
            </div>
          </>
        )}
      </div>
    </section>
  );
}
