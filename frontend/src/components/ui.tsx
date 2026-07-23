import type { ReactNode } from 'react';
import type { Severity } from '../lib/types';

export function SeverityBadge({ severity }: { severity: Severity }) {
  const cls = `sev-${severity.toLowerCase()}`;
  return (
    <span className={`badge ${cls}`}>
      <span className="dot" />
      {severity}
    </span>
  );
}

export function StatusBadge({ status }: { status: string }) {
  const cls = `status-${status.toLowerCase()}`;
  return <span className={`badge ${cls}`}>{humanize(status)}</span>;
}

export function SourceBadge({ source }: { source: string }) {
  const cls = `source-${source.toLowerCase()}`;
  const label = source === 'GooglePlay' ? 'Google Play' : source;
  return <span className={`source-badge ${cls}`}>{label}</span>;
}

export function RatingStars({ rating }: { rating: number | null }) {
  if (rating == null) return <span className="faint" style={{ fontSize: 11.5 }}>no rating</span>;
  const full = Math.max(0, Math.min(5, Math.round(rating)));
  return (
    <span className="rating-stars" title={`${rating} / 5`}>
      {'★'.repeat(full)}
      <span className="empty">{'★'.repeat(5 - full)}</span>
    </span>
  );
}

export function Confidence({ value }: { value: number }) {
  const pct = Math.round(value * 100);
  return (
    <span className="confidence" title={`Confidence ${pct}%`}>
      <span className="track">
        <span className="fill" style={{ width: `${pct}%` }} />
      </span>
      {pct}%
    </span>
  );
}

export function Spinner() {
  return <span className="spinner" aria-label="loading" />;
}

export function LoadingState({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="state">
      <div className="icon">
        <Spinner />
      </div>
      <div className="desc">{label}</div>
    </div>
  );
}

export function EmptyState({
  icon = '∅',
  title,
  desc,
  action,
}: {
  icon?: string;
  title: string;
  desc?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div className="state">
      <div className="icon">{icon}</div>
      <div className="title">{title}</div>
      {desc && <div className="desc">{desc}</div>}
      {action && <div className="mt">{action}</div>}
    </div>
  );
}

export function ErrorState({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const message = error instanceof Error ? error.message : 'Something went wrong.';
  return (
    <div className="error-box">
      <strong>Error:</strong> {message}
      {onRetry && (
        <button className="btn sm ghost" style={{ marginLeft: 10 }} onClick={onRetry}>
          Retry
        </button>
      )}
    </div>
  );
}

export function humanize(s: string): string {
  return s.replace(/([a-z])([A-Z])/g, '$1 $2');
}

export function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return d.toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function relativeTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso).getTime();
  if (Number.isNaN(d)) return '—';
  const diff = Date.now() - d;
  const min = Math.round(diff / 60000);
  if (min < 1) return 'just now';
  if (min < 60) return `${min}m ago`;
  const hr = Math.round(min / 60);
  if (hr < 24) return `${hr}h ago`;
  const day = Math.round(hr / 24);
  if (day < 30) return `${day}d ago`;
  return formatDate(iso);
}
