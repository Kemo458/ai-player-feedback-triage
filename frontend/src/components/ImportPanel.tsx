import { useState } from 'react';
import { useCancelImport, useCreateImport, useImportJob, isImportTerminal } from '../lib/queries';
import type { ImportRequest } from '../lib/queries';
import type { ImportJob, ImportStatus } from '../lib/types';
import { ErrorState, StatusBadge, formatDate } from './ui';
import { useToasts } from './Toasts';

const PROGRESS_WEIGHT: Record<ImportStatus, number> = {
  Queued: 5,
  Fetching: 40,
  Persisting: 75,
  Completed: 100,
  PartiallyCompleted: 100,
  Failed: 100,
  Cancelled: 100,
};

export function ImportPanel({
  gameId,
  defaultUrl,
}: {
  gameId: string;
  defaultUrl?: string | null;
}) {
  const [url, setUrl] = useState(defaultUrl ?? '');
  const [count, setCount] = useState(100);
  const [language, setLanguage] = useState('en');
  const [country, setCountry] = useState('us');
  const [sort, setSort] = useState<'newest' | 'mostRelevant'>('newest');
  const [score, setScore] = useState<string>('');
  const [activeImportId, setActiveImportId] = useState<string | null>(null);

  const create = useCreateImport(gameId);
  const cancel = useCancelImport();
  const job = useImportJob(activeImportId);
  const { push } = useToasts();

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const body: ImportRequest = {
      url: url.trim(),
      count,
      language: language.trim() || 'en',
      country: country.trim() || 'us',
      sort,
      score: score ? Number(score) : null,
    };
    try {
      const created = await create.mutateAsync(body);
      setActiveImportId(created.id);
      push(`Import queued for ${count} review${count === 1 ? '' : 's'}.`, 'success');
    } catch {
      push('Could not start the Google Play import.', 'error');
    }
  };

  // The just-created job appears immediately as Queued even before the poll returns.
  const displayJob: Partial<ImportJob> | undefined =
    job.data ??
    (create.data
      ? {
          id: create.data.id,
          status: create.data.status,
          requestedCount: create.data.requestedCount,
          createdAt: create.data.createdAt,
        }
      : undefined);

  return (
    <section className="panel">
      <div className="panel-head">
        <h3>Import Google Play reviews</h3>
      </div>
      <div className="panel-pad">
        <form onSubmit={submit} className="stack">
          <label className="field">
            Google Play URL
            <input
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              placeholder="https://play.google.com/store/apps/details?id=…"
              required
            />
          </label>
          <div className="form-row">
            <label className="field">
              Count
              <input
                type="number"
                min={1}
                max={500}
                value={count}
                onChange={(e) => setCount(Number(e.target.value))}
              />
            </label>
            <label className="field">
              Language
              <input value={language} onChange={(e) => setLanguage(e.target.value)} />
            </label>
            <label className="field">
              Country
              <input value={country} onChange={(e) => setCountry(e.target.value)} />
            </label>
            <label className="field">
              Sort
              <select value={sort} onChange={(e) => setSort(e.target.value as typeof sort)}>
                <option value="newest">Newest</option>
                <option value="mostRelevant">Most relevant</option>
              </select>
            </label>
            <label className="field">
              Score
              <select value={score} onChange={(e) => setScore(e.target.value)}>
                <option value="">Any</option>
                <option value="1">1★</option>
                <option value="2">2★</option>
                <option value="3">3★</option>
                <option value="4">4★</option>
                <option value="5">5★</option>
              </select>
            </label>
          </div>
          <div className="inline">
            <button className="btn primary" type="submit" disabled={create.isPending}>
              {create.isPending ? 'Starting…' : 'Start import'}
            </button>
            {create.isError && (
              <span className="faint" style={{ color: 'var(--danger)' }}>
                {(create.error as Error).message}
              </span>
            )}
          </div>
        </form>

        {create.isError && <div className="mt"><ErrorState error={create.error} /></div>}

        {displayJob && <ImportJobCard job={displayJob} onCancel={() => cancel.mutate(displayJob.id!)} cancelling={cancel.isPending} />}
      </div>
    </section>
  );
}

function ImportJobCard({
  job,
  onCancel,
  cancelling,
}: {
  job: Partial<ImportJob>;
  onCancel: () => void;
  cancelling: boolean;
}) {
  const status = (job.status ?? 'Queued') as ImportStatus;
  const terminal = isImportTerminal(status);
  const pct = PROGRESS_WEIGHT[status] ?? 5;

  return (
    <div className="import-job">
      <div className="inline wrap" style={{ justifyContent: 'space-between' }}>
        <div className="inline">
          <StatusBadge status={status} />
          <span className="faint" style={{ fontSize: 12 }}>
            requested {job.requestedCount ?? '—'} · started {formatDate(job.createdAt)}
          </span>
        </div>
        {!terminal && (
          <button className="btn sm ghost" onClick={onCancel} disabled={cancelling}>
            {cancelling ? 'Cancelling…' : 'Cancel'}
          </button>
        )}
      </div>

      <div className="progress-track">
        <div className="progress-fill" style={{ width: `${pct}%` }} />
      </div>

      <div className="import-counts">
        <Count n={job.fetchedCount} l="Fetched" />
        <Count n={job.insertedCount} l="Inserted" />
        <Count n={job.updatedCount} l="Updated" />
        <Count n={job.skippedCount} l="Skipped" />
        <Count n={job.failedCount} l="Failed" />
      </div>

      {job.lastErrorMessage && (
        <div className="error-box mt">
          {job.lastErrorCode ? `[${job.lastErrorCode}] ` : ''}
          {job.lastErrorMessage}
        </div>
      )}
    </div>
  );
}

function Count({ n, l }: { n: number | undefined; l: string }) {
  return (
    <div className="c">
      <div className="n">{n ?? 0}</div>
      <div className="l">{l}</div>
    </div>
  );
}
