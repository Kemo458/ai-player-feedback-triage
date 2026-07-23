import { useState } from 'react';
import { useParams, useSearchParams } from 'react-router-dom';
import { usePublicStatus, usePublicSubmit } from '../lib/queries';
import { StatusBadge } from '../components/ui';

export function SubmitPage() {
  const { gameId = '' } = useParams();
  const [params] = useSearchParams();
  const token = params.get('token') ?? '';

  const [text, setText] = useState('');
  const [rating, setRating] = useState<string>('');
  const [appVersion, setAppVersion] = useState('');
  const [device, setDevice] = useState('');
  const [locale, setLocale] = useState('');
  const [submittedId, setSubmittedId] = useState<string | null>(null);

  const submit = usePublicSubmit(gameId, token);
  const statusQuery = usePublicStatus(submittedId);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const res = await submit.mutateAsync({
      text: text.trim(),
      rating: rating ? Number(rating) : undefined,
      appVersion: appVersion.trim() || undefined,
      device: device.trim() || undefined,
      locale: locale.trim() || undefined,
    });
    setSubmittedId(res.id);
  };

  if (!token) {
    return (
      <div className="auth-wrap">
        <div className="auth-card">
          <div className="logo-lg">◆</div>
          <h1>Invalid link</h1>
          <p className="sub">
            This submission link is missing its access token. Please use the full link provided by
            the game team.
          </p>
        </div>
      </div>
    );
  }

  if (submittedId) {
    const status = statusQuery.data?.status;
    return (
      <div className="auth-wrap">
        <div className="auth-card">
          <div className="logo-lg">✓</div>
          <h1>Thanks for your feedback!</h1>
          <p className="sub">
            Your report was received and is being processed. You can keep this page open to watch
            its status.
          </p>
          <div className="panel panel-pad">
            <dl className="kv">
              <dt>Report ID</dt>
              <dd className="mono">{submittedId}</dd>
              <dt>Status</dt>
              <dd>{status ? <StatusBadge status={status} /> : 'Checking…'}</dd>
            </dl>
          </div>
          <button
            className="btn ghost block mt"
            onClick={() => {
              setSubmittedId(null);
              setText('');
              setRating('');
              setAppVersion('');
              setDevice('');
              setLocale('');
            }}
          >
            Submit another
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="auth-wrap">
      <div className="auth-card" style={{ maxWidth: 460 }}>
        <div className="logo-lg">◆</div>
        <h1>Share your feedback</h1>
        <p className="sub">Tell the team what's working and what isn't. No account needed.</p>
        <form className="auth-form" onSubmit={onSubmit}>
          <label className="field">
            Your feedback
            <textarea
              value={text}
              onChange={(e) => setText(e.target.value)}
              placeholder="Describe the bug, feature idea, or anything else…"
              minLength={3}
              maxLength={5000}
              required
            />
          </label>
          <div className="form-row">
            <label className="field">
              Rating
              <select value={rating} onChange={(e) => setRating(e.target.value)}>
                <option value="">No rating</option>
                <option value="1">1★</option>
                <option value="2">2★</option>
                <option value="3">3★</option>
                <option value="4">4★</option>
                <option value="5">5★</option>
              </select>
            </label>
            <label className="field">
              App version
              <input
                value={appVersion}
                onChange={(e) => setAppVersion(e.target.value)}
                placeholder="1.4.2"
              />
            </label>
          </div>
          <div className="form-row">
            <label className="field">
              Device
              <input
                value={device}
                onChange={(e) => setDevice(e.target.value)}
                placeholder="Pixel 8"
              />
            </label>
            <label className="field">
              Locale
              <input
                value={locale}
                onChange={(e) => setLocale(e.target.value)}
                placeholder="en-US"
              />
            </label>
          </div>
          {submit.isError && (
            <div className="error-box">{(submit.error as Error).message}</div>
          )}
          <button className="btn primary block" type="submit" disabled={submit.isPending}>
            {submit.isPending ? 'Submitting…' : 'Submit feedback'}
          </button>
        </form>
      </div>
    </div>
  );
}
