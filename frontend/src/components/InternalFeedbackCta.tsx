import { useState } from 'react';
import { useGenerateSubmissionToken } from '../lib/queries';
import { useToasts } from './Toasts';

interface Props {
  gameId: string;
  gameName: string;
  submissionToken?: string | null;
}

export function InternalFeedbackCta({ gameId, gameName, submissionToken }: Props) {
  const retrieve = useGenerateSubmissionToken(gameId);
  const [retrievedToken, setRetrievedToken] = useState<string>();
  const [copied, setCopied] = useState(false);
  const { push } = useToasts();
  const token = submissionToken ?? retrievedToken;

  const link = token
    ? `${window.location.origin}/submit/${gameId}?token=${encodeURIComponent(token)}`
    : '';

  const retrieveLink = async () => {
    try {
      const game = await retrieve.mutateAsync();
      if (!game.submissionToken) throw new Error('No submission token returned.');
      setRetrievedToken(game.submissionToken);
      push('Permanent internal-feedback link is ready.', 'success');
    } catch {
      push('Could not retrieve the internal-feedback link.', 'error');
    }
  };

  const copyLink = async () => {
    try {
      await navigator.clipboard.writeText(link);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1600);
      push('Internal-feedback link copied.', 'success');
    } catch {
      push('Copy was blocked. Select the link manually.', 'warning');
    }
  };

  return (
    <section className="internal-feedback-cta reveal" aria-label="Internal player feedback">
      <div className="internal-feedback-icon" aria-hidden="true">
        ✓
      </div>
      <div className="internal-feedback-copy">
        <span className="tele">Core collection channel</span>
        <h2>Collect internal player feedback</h2>
        <p>
          Share a dedicated form for “{gameName}” with beta testers, QA, or your player
          community—separate from Google Play reviews.
        </p>
      </div>

      {token ? (
        <div className="internal-feedback-link">
          <label className="field">
            Shareable submit link
            <input readOnly value={link} onFocus={(event) => event.target.select()} />
          </label>
          <div className="inline">
            <button className="btn primary" onClick={copyLink}>
              {copied ? 'Copied ✓' : 'Copy link'}
            </button>
            <a className="btn ghost" href={link} rel="noreferrer" target="_blank">
              Open form ↗
            </a>
          </div>
          <span className="internal-feedback-once">
            One permanent link for this game. You can copy it again after any reload.
          </span>
        </div>
      ) : (
        <div className="internal-feedback-action">
          <button
            className="btn primary internal-feedback-button"
            disabled={retrieve.isPending}
            onClick={retrieveLink}
          >
            {retrieve.isPending ? 'Loading link…' : 'Show permanent link'}
          </button>
          <span>This game has one stable internal-feedback link. It never rotates.</span>
        </div>
      )}
    </section>
  );
}
