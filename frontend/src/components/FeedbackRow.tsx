import { useEffect, useRef, useState } from 'react';
import type { Feedback } from '../lib/types';
import { useMarkReviewed, useQueuePosition, useRetryFeedback } from '../lib/queries';
import {
  Confidence,
  RatingStars,
  SeverityBadge,
  SourceBadge,
  StatusBadge,
  formatDate,
  relativeTime,
} from './ui';
import { useToasts } from './Toasts';

function isToxic(fb: Feedback): boolean {
  const a = fb.analysis;
  if (!a) return false;
  return a.tags.includes('Toxic') || a.toxicity === 'Toxic';
}

function fmtWait(sec: number): string {
  if (sec <= 0) return 'a moment';
  if (sec < 60) return 'under a minute';
  if (sec < 3600) return `~${Math.round(sec / 60)} min`;
  const h = Math.floor(sec / 3600);
  const m = Math.round((sec % 3600) / 60);
  return m > 0 ? `~${h}h ${m}m` : `~${h}h`;
}

// Live "why isn't this analyzed yet" line for items still in the queue.
function QueueStatus({ feedbackId }: { feedbackId: string }) {
  const q = useQueuePosition(feedbackId, true);
  const d = q.data;

  if (!d) {
    return <div className="queue-status faint">checking queue position…</div>;
  }

  if (d.status === 'Processing') {
    return (
      <div className="queue-status">
        <span className="q-dot analyzing" /> analyzing now — should finish in ~{d.avgItemSeconds}s
      </div>
    );
  }

  const lead =
    d.aheadCount === 0
      ? 'Next up — starts within moments'
      : `Queued · ${d.aheadCount} item${d.aheadCount === 1 ? '' : 's'} ahead across all games · est. wait ${fmtWait(
          d.estimatedWaitSeconds,
        )}`;

  return (
    <div className="queue-status">
      <span className="q-dot waiting" /> {lead}
      <span className="faint">
        {' '}
        · {d.globalProcessing} analyzing, {d.globalPending} pending overall · CPU model ~
        {d.avgItemSeconds}s/item, one at a time
      </span>
    </div>
  );
}

export function FeedbackRow({
  feedback,
  gameId,
  expanded,
  selected,
  onSelect,
  onToggle,
}: {
  feedback: Feedback;
  gameId: string;
  expanded: boolean;
  selected: boolean;
  onSelect: () => void;
  onToggle: () => void;
}) {
  const [revealToxic, setRevealToxic] = useState(false);
  const [justCompleted, setJustCompleted] = useState(false);
  const previousStatus = useRef(feedback.status);
  const retry = useRetryFeedback(gameId);
  const markReviewed = useMarkReviewed(gameId);
  const { push } = useToasts();

  useEffect(() => {
    if (previousStatus.current !== 'Completed' && feedback.status === 'Completed') {
      setJustCompleted(true);
      const timer = window.setTimeout(() => setJustCompleted(false), 1100);
      previousStatus.current = feedback.status;
      return () => window.clearTimeout(timer);
    }
    previousStatus.current = feedback.status;
  }, [feedback.status]);

  const a = feedback.analysis;
  const toxic = isToxic(feedback);
  const canRetry = feedback.status === 'Failed' || feedback.status === 'ManualReview';
  const canReview = feedback.status === 'ManualReview' || feedback.status === 'Failed';

  const hasSummary = !!a?.summary;

  return (
    <article
      className={`feed-row ${selected ? 'selected' : ''} ${justCompleted ? 'just-completed' : ''}`}
      id={`feedback-${feedback.id}`}
      onClick={onSelect}
      onFocus={onSelect}
      tabIndex={selected ? 0 : -1}
    >
      <div className="top">
        <SourceBadge source={feedback.source} />
        {feedback.author ? (
          <span className="author" title={`Reviewer: ${feedback.author}`}>
            <span className="a-glyph">◆</span>
            {feedback.author}
          </span>
        ) : (
          feedback.source === 'Internal' && <span className="author anon">anonymous</span>
        )}
        <RatingStars rating={feedback.rating} />
        {a && <SeverityBadge severity={a.severity} />}
        {a?.sentiment && <span className="chip">{a.sentiment}</span>}
        <span className="spacer" />
        <StatusBadge status={feedback.status} />
      </div>

      {hasSummary ? (
        <>
          <div className="summary">{a!.summary}</div>
          <div className="orig-quote clamp2" title={feedback.text}>“{feedback.text}”</div>
        </>
      ) : (
        <div className="summary clamp2" title={feedback.text}>{feedback.text}</div>
      )}

      {(feedback.status === 'Pending' ||
        feedback.status === 'RetryScheduled' ||
        feedback.status === 'Processing') && <QueueStatus feedbackId={feedback.id} />}

      {a && a.tags.length > 0 && (
        <div className="chip-row" style={{ marginBottom: 8 }}>
          {a.tags.map((t) => (
            <span className="chip accent" key={t}>
              {t}
            </span>
          ))}
          {a.entities.slice(0, 4).map((e, i) => (
            <span className="chip entity" key={`${e.normalizedName}-${i}`}>
              {e.normalizedName}
            </span>
          ))}
          {a.entities.length > 4 && (
            <span className="chip">+{a.entities.length - 4} more</span>
          )}
        </div>
      )}

      <div className="meta">
        {a && <Confidence value={a.confidence} />}
        {feedback.appVersion && <span className="mono">v{feedback.appVersion}</span>}
        {feedback.device && <span>{feedback.device}</span>}
        <span title={formatDate(feedback.sourceCreatedAt ?? feedback.importedAt)}>
          {relativeTime(feedback.sourceCreatedAt ?? feedback.importedAt)}
        </span>
        <span className="spacer" />
        <button
          className="btn sm ghost"
          onClick={(event) => {
            event.stopPropagation();
            onToggle();
          }}
        >
          {expanded ? 'Collapse' : 'Details'}
        </button>
        {canReview && (
          <button
            className="btn sm"
            onClick={(event) => {
              event.stopPropagation();
              markReviewed.mutate(feedback.id, {
                onSuccess: () => push('Feedback marked as reviewed.', 'success'),
                onError: () => push('Could not mark feedback as reviewed.', 'error'),
              });
            }}
            disabled={markReviewed.isPending}
          >
            Mark reviewed
          </button>
        )}
        {canRetry && (
          <button
            className="btn sm"
            onClick={(event) => {
              event.stopPropagation();
              retry.mutate(feedback.id, {
                onSuccess: () => push('Feedback queued for retry.', 'success'),
                onError: () => push('Could not retry this feedback.', 'error'),
              });
            }}
            disabled={retry.isPending}
          >
            {retry.isPending ? 'Retrying…' : 'Retry'}
          </button>
        )}
      </div>

      {expanded && (
        <div className="expand-region">
          {/* Original player comment — toxic content stays behind an explicit reveal. */}
          <div className="tele" style={{ marginBottom: 6 }}>Original feedback</div>
          {toxic && !revealToxic ? (
            <div className="toxic-guard">
              ⚠ This feedback may contain potentially toxic content.
              <button
                className="btn sm ghost"
                onClick={(event) => {
                  event.stopPropagation();
                  setRevealToxic(true);
                }}
              >
                Show potentially toxic content
              </button>
            </div>
          ) : (
            <div className="raw-text">{feedback.text}</div>
          )}

          <dl className="kv">
            {feedback.author && (
              <>
                <dt>Reviewer</dt>
                <dd>{feedback.author}</dd>
              </>
            )}
            <dt>Feedback ID</dt>
            <dd className="mono">{feedback.id}</dd>
            {feedback.externalId && (
              <>
                <dt>External ID</dt>
                <dd className="mono">{feedback.externalId}</dd>
              </>
            )}
            <dt>Imported</dt>
            <dd>{formatDate(feedback.importedAt)}</dd>
            <dt>Attempts</dt>
            <dd>{feedback.attemptCount}</dd>
            {feedback.lastErrorMessage && (
              <>
                <dt>Last error</dt>
                <dd style={{ color: 'var(--danger)' }}>
                  {feedback.lastErrorCode ? `[${feedback.lastErrorCode}] ` : ''}
                  {feedback.lastErrorMessage}
                </dd>
              </>
            )}
            {a && (
              <>
                <dt>Category</dt>
                <dd>{a.primaryCategory}</dd>
                <dt>Toxicity</dt>
                <dd>{a.toxicity}</dd>
                <dt>Provider</dt>
                <dd>
                  {a.provider} · {a.model}
                </dd>
              </>
            )}
          </dl>

          {a && a.entities.length > 0 && (
            <div className="mt">
              <div className="faint" style={{ fontSize: 11.5, marginBottom: 6 }}>
                ALL ENTITIES
              </div>
              <div className="chip-row">
                {a.entities.map((e, i) => (
                  <span className="chip entity" key={`${e.normalizedName}-full-${i}`} title={e.evidence}>
                    {e.normalizedName}
                    <span className="faint">· {e.type}</span>
                  </span>
                ))}
              </div>
            </div>
          )}
        </div>
      )}
    </article>
  );
}
