import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useDashboard, useGame } from '../lib/queries';
import { useSignalR } from '../lib/useSignalR';
import type {
  FeedbackFilters,
  FeedbackStatus,
  Sentiment,
  Severity,
  Source,
  SourceTab,
  Tag,
  ViewKey,
} from '../lib/types';
import { TopBar } from '../components/TopBar';
import { ConnectionPill } from '../components/ConnectionPill';
import { StatsCards, type DashboardMetric } from '../components/StatsCards';
import { PipelineFlow } from '../components/PipelineFlow';
import { ActivityConsole } from '../components/ActivityConsole';
import { SummaryPanel } from '../components/SummaryPanel';
import { EntitiesPanel } from '../components/EntitiesPanel';
import { IngestionDrawer } from '../components/IngestionDrawer';
import { InternalFeedbackCta } from '../components/InternalFeedbackCta';
import { FeedbackList } from '../components/FeedbackList';
import { ErrorState, LoadingState } from '../components/ui';

const VIEW_MAP: Record<ViewKey, { tag?: Tag; severity?: Severity }> = {
  critical: { tag: 'Bug', severity: 'Critical' },
  feature: { tag: 'Feature' },
  lore: { tag: 'Lore' },
  toxic: { tag: 'Toxic' },
  all: {},
};

const VIEW_LABELS: { key: ViewKey; label: string }[] = [
  { key: 'critical', label: 'Critical bugs' },
  { key: 'feature', label: 'Feature requests' },
  { key: 'lore', label: 'Lore questions' },
  { key: 'toxic', label: 'Toxic complaints' },
  { key: 'all', label: 'All feedback' },
];

type StatusFilter = FeedbackStatus | 'Active' | '';

export function DashboardPage() {
  const { gameId = '' } = useParams();
  const game = useGame(gameId);
  const dashboard = useDashboard(gameId);
  const connection = useSignalR(gameId);

  const [sourceTab, setSourceTab] = useState<SourceTab>('all');
  const [view, setView] = useState<ViewKey>('all');
  const [search, setSearch] = useState('');
  const [searchInput, setSearchInput] = useState('');
  const [severity, setSeverity] = useState<Severity | ''>('');
  const [sentiment, setSentiment] = useState<Sentiment | ''>('');
  const [status, setStatus] = useState<StatusFilter>('');
  const [entity, setEntity] = useState('');
  const [sort, setSort] = useState('priority');
  const [ingestionOpen, setIngestionOpen] = useState(false);
  const searchRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    const timer = window.setTimeout(() => setSearch(searchInput.trim()), 280);
    return () => window.clearTimeout(timer);
  }, [searchInput]);

  useEffect(() => {
    const onSearchShortcut = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      if (
        event.key !== '/' ||
        target?.matches('input, textarea, select') ||
        target?.isContentEditable
      ) {
        return;
      }
      event.preventDefault();
      searchRef.current?.focus();
    };
    window.addEventListener('keydown', onSearchShortcut);
    return () => window.removeEventListener('keydown', onSearchShortcut);
  }, []);

  const source: Source | undefined = sourceTab === 'all' ? undefined : sourceTab;
  const viewFilter = VIEW_MAP[view];

  const filters: FeedbackFilters = useMemo(
    () => ({
      source,
      tag: viewFilter.tag,
      severity: (severity || viewFilter.severity) as Severity | undefined,
      sentiment: sentiment || undefined,
      status: status || undefined,
      entity: entity || undefined,
      search: search || undefined,
      sort,
    }),
    [
      entity,
      search,
      sentiment,
      severity,
      sort,
      source,
      status,
      viewFilter.severity,
      viewFilter.tag,
    ],
  );

  const summaryParams = {
    source,
    tag: viewFilter.tag,
    severity: (severity || viewFilter.severity) as string | undefined,
  };

  const clearFilters = () => {
    setView('all');
    setSeverity('');
    setSentiment('');
    setStatus('');
    setEntity('');
    setSearch('');
    setSearchInput('');
    setSort('priority');
  };

  const activeMetric: DashboardMetric | undefined =
    view === 'critical' && !status
      ? 'critical'
      : view === 'toxic' && !status
        ? 'toxic'
        : status === 'Active'
          ? 'active'
          : status === 'ManualReview'
            ? 'manual'
            : status === 'Failed'
              ? 'failed'
              : view === 'all' &&
                  !status &&
                  !severity &&
                  !sentiment &&
                  !entity &&
                  !search
                ? 'all'
                : undefined;

  const selectMetric = (metric: DashboardMetric) => {
    clearFilters();
    if (metric === 'active') setStatus('Active');
    if (metric === 'critical') setView('critical');
    if (metric === 'toxic') setView('toxic');
    if (metric === 'manual') setStatus('ManualReview');
    if (metric === 'failed') setStatus('Failed');
    window.requestAnimationFrame(() => {
      document.getElementById('feedback-workspace')?.scrollIntoView({
        behavior: 'smooth',
        block: 'start',
      });
    });
  };

  const applySearch = (event: React.FormEvent) => {
    event.preventDefault();
    setSearch(searchInput.trim());
  };

  if (game.isLoading) {
    return (
      <div className="app-shell">
        <TopBar />
        <div className="page">
          <LoadingState label="Loading game…" />
        </div>
      </div>
    );
  }

  const hasActiveFilters =
    view !== 'all' ||
    !!severity ||
    !!sentiment ||
    !!status ||
    !!entity ||
    !!search ||
    sort !== 'priority';

  return (
    <div className="app-shell">
      <TopBar right={<ConnectionPill status={connection} />} />
      <div className="page wide">
        <div className="crumbs">
          <Link to="/">Games</Link>
          <span className="sep">/</span>
          <span>{game.data?.name ?? gameId}</span>
        </div>

        <div className="game-header reveal">
          <div>
            <span className="tele">Triage workspace</span>
            <h1>{game.data?.name ?? 'Dashboard'}</h1>
          </div>
          <div className="game-header-actions">
            <div className="tabs" aria-label="Feedback source">
              {(['all', 'GooglePlay', 'Internal'] as SourceTab[]).map((sourceOption) => (
                <button
                  className={sourceTab === sourceOption ? 'active' : ''}
                  key={sourceOption}
                  onClick={() => setSourceTab(sourceOption)}
                >
                  {sourceOption === 'GooglePlay'
                    ? 'Google Play'
                    : sourceOption === 'Internal'
                      ? 'Internal'
                      : 'All'}
                </button>
              ))}
            </div>
            <button className="btn primary" onClick={() => setIngestionOpen(true)}>
              Sources &amp; ingestion
            </button>
          </div>
        </div>

        <div className="mt">
          <InternalFeedbackCta
            gameId={gameId}
            gameName={game.data?.name ?? 'this game'}
            submissionToken={game.data?.submissionToken}
          />
        </div>

        {dashboard.isLoading ? (
          <div className="mt">
            <LoadingState label="Loading dashboard…" />
          </div>
        ) : dashboard.isError ? (
          <div className="mt">
            <ErrorState error={dashboard.error} onRetry={() => dashboard.refetch()} />
          </div>
        ) : dashboard.data ? (
          <section className="attention-zone mt reveal">
            <div className="attention-head">
              <div>
                <span className="tele">Needs attention</span>
                <p>Choose a metric to jump directly into matching feedback.</p>
              </div>
              <span className="keyboard-hint">
                / search · J/K navigate · Enter details · R review
              </span>
            </div>
            <StatsCards
              activeMetric={activeMetric}
              dashboard={dashboard.data}
              onSelect={selectMetric}
            />
          </section>
        ) : null}

        <div className="mt">
          <PipelineFlow gameId={gameId} />
        </div>

        <div className="mt">
          <SummaryPanel
            gameId={gameId}
            onThemeSelect={(theme) => {
              setSearchInput(theme);
              setSearch(theme);
            }}
            params={summaryParams}
          />
        </div>

        <div className="section-title" id="feedback-workspace">
          <h2>Feedback workspace</h2>
          <span className="keyboard-hint">Source: {sourceTab === 'all' ? 'All' : sourceTab}</span>
        </div>

        <div className="grid dashboard-cols">
          <div className="stack">
            <div className="panel panel-pad filter-panel">
              <div className="toolbar">
                <label className="field" style={{ minWidth: 190 }}>
                  View
                  <select value={view} onChange={(event) => setView(event.target.value as ViewKey)}>
                    {VIEW_LABELS.map((option) => (
                      <option key={option.key} value={option.key}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                </label>

                <form className="grow" onSubmit={applySearch}>
                  <label className="field">
                    Search
                    <input
                      placeholder="Search feedback…  (press /)"
                      ref={searchRef}
                      value={searchInput}
                      onChange={(event) => setSearchInput(event.target.value)}
                    />
                  </label>
                </form>
              </div>

              <div className="toolbar mt">
                <label className="field">
                  Severity
                  <select
                    value={severity}
                    onChange={(event) => setSeverity(event.target.value as Severity | '')}
                  >
                    <option value="">Any</option>
                    {(['Critical', 'High', 'Medium', 'Low', 'Unknown'] as Severity[]).map(
                      (option) => (
                        <option key={option} value={option}>
                          {option}
                        </option>
                      ),
                    )}
                  </select>
                </label>

                <label className="field">
                  Sentiment
                  <select
                    value={sentiment}
                    onChange={(event) => setSentiment(event.target.value as Sentiment | '')}
                  >
                    <option value="">Any</option>
                    {(['Positive', 'Neutral', 'Negative', 'Mixed'] as Sentiment[]).map(
                      (option) => (
                        <option key={option} value={option}>
                          {option}
                        </option>
                      ),
                    )}
                  </select>
                </label>

                <label className="field">
                  Status
                  <select
                    value={status}
                    onChange={(event) => setStatus(event.target.value as StatusFilter)}
                  >
                    <option value="">Any</option>
                    <option value="Active">Active queue</option>
                    {(
                      [
                        'Pending',
                        'Processing',
                        'Completed',
                        'RetryScheduled',
                        'ManualReview',
                        'Failed',
                      ] as FeedbackStatus[]
                    ).map((option) => (
                      <option key={option} value={option}>
                        {option}
                      </option>
                    ))}
                  </select>
                </label>

                <label className="field">
                  Sort
                  <select value={sort} onChange={(event) => setSort(event.target.value)}>
                    <option value="priority">Priority</option>
                    <option value="">Newest feedback</option>
                    <option value="importedAt">Oldest imported</option>
                    <option value="-rating">Highest rating</option>
                    <option value="rating">Lowest rating</option>
                    <option value="confidence">Lowest confidence</option>
                  </select>
                </label>
              </div>

              <div className="toolbar mt quick-filters">
                <button
                  className={`btn sm ${status === 'Failed' ? 'primary' : 'ghost'}`}
                  onClick={() => setStatus((current) => (current === 'Failed' ? '' : 'Failed'))}
                >
                  Failures
                </button>
                <button
                  className={`btn sm ${status === 'ManualReview' ? 'primary' : 'ghost'}`}
                  onClick={() =>
                    setStatus((current) => (current === 'ManualReview' ? '' : 'ManualReview'))
                  }
                >
                  Manual review
                </button>
                {hasActiveFilters && (
                  <button className="btn sm ghost" onClick={clearFilters}>
                    Clear filters
                  </button>
                )}
              </div>

              {hasActiveFilters && (
                <div className="active-filters">
                  <span className="tele">Active</span>
                  {view !== 'all' && (
                    <button onClick={() => setView('all')}>
                      View: {VIEW_LABELS.find((option) => option.key === view)?.label} ×
                    </button>
                  )}
                  {severity && (
                    <button onClick={() => setSeverity('')}>Severity: {severity} ×</button>
                  )}
                  {sentiment && (
                    <button onClick={() => setSentiment('')}>Sentiment: {sentiment} ×</button>
                  )}
                  {status && <button onClick={() => setStatus('')}>Status: {status} ×</button>}
                  {entity && <button onClick={() => setEntity('')}>Entity: {entity} ×</button>}
                  {search && (
                    <button
                      onClick={() => {
                        setSearch('');
                        setSearchInput('');
                      }}
                    >
                      Search: “{search}” ×
                    </button>
                  )}
                  {sort !== 'priority' && (
                    <button onClick={() => setSort('priority')}>Custom sorting ×</button>
                  )}
                </div>
              )}
            </div>

            <FeedbackList filters={filters} gameId={gameId} />
          </div>

          <div className="stack">
            <ActivityConsole gameId={gameId} />
            <EntitiesPanel
              gameId={gameId}
              onSelect={(name) => setEntity((current) => (current === name ? '' : name))}
              selected={entity}
              source={source}
            />
          </div>
        </div>
      </div>

      <IngestionDrawer
        defaultUrl={game.data?.googlePlayUrl}
        gameId={gameId}
        gameName={game.data?.name ?? 'Game'}
        submissionToken={game.data?.submissionToken}
        onClose={() => setIngestionOpen(false)}
        open={ingestionOpen}
      />
    </div>
  );
}
