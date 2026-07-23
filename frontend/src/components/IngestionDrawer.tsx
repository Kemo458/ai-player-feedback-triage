import { useEffect, useState } from 'react';
import { useGenerateSubmissionToken } from '../lib/queries';
import { SubmissionTokenCallout } from '../pages/GamesPage';
import { ImportPanel } from './ImportPanel';
import { useToasts } from './Toasts';

interface Props {
  gameId: string;
  gameName: string;
  defaultUrl?: string | null;
  submissionToken?: string | null;
  open: boolean;
  onClose: () => void;
}

export function IngestionDrawer({
  gameId,
  gameName,
  defaultUrl,
  submissionToken,
  open,
  onClose,
}: Props) {
  const genToken = useGenerateSubmissionToken(gameId);
  const [retrievedToken, setRetrievedToken] = useState<string>();
  const token = submissionToken ?? retrievedToken;
  const { push } = useToasts();

  useEffect(() => {
    if (!open) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [onClose, open]);

  if (!open) return null;

  const retrieveLink = async () => {
    try {
      const game = await genToken.mutateAsync();
      if (game.submissionToken) {
        setRetrievedToken(game.submissionToken);
        push('Permanent feedback link retrieved.', 'success');
      }
    } catch {
      push('Could not retrieve the public feedback link.', 'error');
    }
  };

  return (
    <div className="drawer-backdrop" onMouseDown={onClose}>
      <aside
        aria-label="Sources and ingestion"
        aria-modal="true"
        className="ingestion-drawer"
        onMouseDown={(event) => event.stopPropagation()}
        role="dialog"
      >
        <div className="drawer-head">
          <div>
            <span className="tele">Sources &amp; ingestion</span>
            <h2>{gameName}</h2>
          </div>
          <button aria-label="Close ingestion drawer" className="btn sm ghost" onClick={onClose}>
            Close
          </button>
        </div>

        <div className="drawer-body">
          <ImportPanel gameId={gameId} defaultUrl={defaultUrl} />

          <section className="panel">
            <div className="panel-head">
              <div>
                <h3>Public feedback link</h3>
                <span className="sub">For beta testers and players</span>
              </div>
            </div>
            <div className="panel-pad">
              {token ? (
                <SubmissionTokenCallout
                  gameId={gameId}
                  gameName={gameName}
                  submissionToken={token}
                  defaultOpen
                />
              ) : (
                <>
                  <p className="drawer-copy">
                    Open the permanent game-specific link for collecting internal player feedback.
                  </p>
                  <button
                    className="btn primary"
                    disabled={genToken.isPending}
                    onClick={retrieveLink}
                  >
                    {genToken.isPending ? 'Loading…' : 'Show permanent link'}
                  </button>
                </>
              )}
            </div>
          </section>
        </div>
      </aside>
    </div>
  );
}
