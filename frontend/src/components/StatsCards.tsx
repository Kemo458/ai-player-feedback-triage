import { useEffect, useRef, useState, type ReactNode } from 'react';
import type { Dashboard } from '../lib/types';

export type DashboardMetric =
  | 'all'
  | 'active'
  | 'critical'
  | 'toxic'
  | 'manual'
  | 'failed';

interface Props {
  dashboard: Dashboard;
  activeMetric?: DashboardMetric;
  onSelect: (metric: DashboardMetric) => void;
}

function AnimatedNumber({
  children,
  tone = '',
}: {
  children: number;
  tone?: string;
}) {
  const previous = useRef(children);
  const [changed, setChanged] = useState(false);

  useEffect(() => {
    if (previous.current === children) return;
    previous.current = children;
    setChanged(true);
    const timer = window.setTimeout(() => setChanged(false), 650);
    return () => window.clearTimeout(timer);
  }, [children]);

  return (
    <div className={`value ${tone} ${changed ? 'metric-bump' : ''}`}>
      {children.toLocaleString()}
    </div>
  );
}

function MetricCard({
  label,
  value,
  hint,
  tone,
  metric,
  active,
  onSelect,
}: {
  label: string;
  value: number;
  hint: ReactNode;
  tone?: string;
  metric: DashboardMetric;
  active: boolean;
  onSelect: (metric: DashboardMetric) => void;
}) {
  return (
    <button
      aria-pressed={active}
      className={`stat-card actionable ${active ? 'selected' : ''}`}
      onClick={() => onSelect(metric)}
      type="button"
    >
      <div className="label">{label}</div>
      <AnimatedNumber tone={tone}>{value}</AnimatedNumber>
      <div className="hint">{hint}</div>
      <span className="metric-action">Filter feedback →</span>
    </button>
  );
}

export function StatsCards({ dashboard, activeMetric, onSelect }: Props) {
  const p = dashboard.processing;
  const active = p.pending + p.processing + p.retryScheduled;
  const ratings = dashboard.ratingDistribution ?? {};
  const maxRating = Math.max(1, ...Object.values(ratings));

  return (
    <div className="stats-row">
      <MetricCard
        active={activeMetric === 'all'}
        hint={`${dashboard.totals.bySource.GooglePlay} Google Play · ${dashboard.totals.bySource.Internal} internal`}
        label="Total feedback"
        metric="all"
        onSelect={onSelect}
        value={dashboard.totals.total}
      />
      <MetricCard
        active={activeMetric === 'active'}
        hint={`${p.processing} analyzing · ${p.retryScheduled} retrying`}
        label="Active queue"
        metric="active"
        onSelect={onSelect}
        value={active}
      />
      <MetricCard
        active={activeMetric === 'critical'}
        hint="Urgent product impact"
        label="Critical bugs"
        metric="critical"
        onSelect={onSelect}
        tone="danger"
        value={dashboard.criticalBugs}
      />
      <MetricCard
        active={activeMetric === 'manual'}
        hint="Needs a human decision"
        label="Manual review"
        metric="manual"
        onSelect={onSelect}
        tone="purple"
        value={p.manualReview}
      />
      <MetricCard
        active={activeMetric === 'toxic'}
        hint="Potentially harmful content"
        label="Toxic"
        metric="toxic"
        onSelect={onSelect}
        tone="warn"
        value={dashboard.toxic}
      />
      <MetricCard
        active={activeMetric === 'failed'}
        hint="Retry or investigate"
        label="Failures"
        metric="failed"
        onSelect={onSelect}
        tone="danger"
        value={p.failed}
      />

      <div className="stat-card rating-card">
        <div className="label">Rating distribution</div>
        <div className="rating-bars">
          {[5, 4, 3, 2, 1].map((star) => {
            const n = ratings[String(star)] ?? 0;
            return (
              <div className="rating-bar" key={star}>
                <span className="star">{star}★</span>
                <span className="track">
                  <span className="fill" style={{ width: `${(n / maxRating) * 100}%` }} />
                </span>
                <span className="n">{n}</span>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
