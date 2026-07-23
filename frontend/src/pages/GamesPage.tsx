import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useCreateGame, useDeleteGame, useGames } from '../lib/queries';
import type { Game } from '../lib/types';
import { TopBar } from '../components/TopBar';
import { EmptyState, ErrorState, LoadingState, formatDate } from '../components/ui';

export function GamesPage() {
  const navigate = useNavigate();
  const games = useGames();
  const createGame = useCreateGame();
  const [name, setName] = useState('');
  const [url, setUrl] = useState('');

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const game = await createGame.mutateAsync({
      name: name.trim(),
      googlePlayUrl: url.trim() || undefined,
    });
    setName('');
    setUrl('');
    // Go straight to the new game's dashboard. With a Google Play URL the server has already
    // started importing; the internal-feedback link is generated there on demand if needed.
    navigate(`/games/${game.id}`);
  };

  const items = games.data?.items ?? [];

  return (
    <div className="app-shell">
      <TopBar />
      <div className="page">
        <div className="section-title">
          <h2>Games</h2>
        </div>

        <section className="panel panel-pad">
          <h3 style={{ marginBottom: 12 }}>Create a game</h3>
          <form onSubmit={submit} className="toolbar">
            <div className="grow">
              <input
                placeholder="Game name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
              />
            </div>
            <div className="grow">
              <input
                placeholder="Google Play URL (optional)"
                value={url}
                onChange={(e) => setUrl(e.target.value)}
              />
            </div>
            <button className="btn primary" type="submit" disabled={createGame.isPending}>
              {createGame.isPending ? 'Creating…' : 'Create game'}
            </button>
          </form>
          {createGame.isError && (
            <div className="mt">
              <ErrorState error={createGame.error} />
            </div>
          )}
        </section>

        {games.isLoading ? (
          <LoadingState label="Loading games…" />
        ) : games.isError ? (
          <div className="mt">
            <ErrorState error={games.error} onRetry={() => games.refetch()} />
          </div>
        ) : items.length === 0 ? (
          <EmptyState
            icon="🎮"
            title="No games yet"
            desc="Create your first game above to start triaging player feedback."
          />
        ) : (
          <div className="games-grid">
            {items.map((game) => (
              <GameCard game={game} key={game.id} />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function GameCard({ game }: { game: Game }) {
  const del = useDeleteGame();
  const [confirming, setConfirming] = useState(false);
  const [artworkFailed, setArtworkFailed] = useState(false);
  const initials = game.name.trim().slice(0, 2).toUpperCase() || 'GG';

  return (
    <div className="game-card">
      <Link className="gc-link" to={`/games/${game.id}`}>
        <div className="game-card-main">
          <div className="game-artwork" aria-hidden="true">
            {game.iconUrl && !artworkFailed ? (
              <img
                alt=""
                loading="lazy"
                onError={() => setArtworkFailed(true)}
                src={game.iconUrl}
              />
            ) : (
              <span>{initials}</span>
            )}
          </div>
          <div className="game-card-copy">
            <div className="name">{game.name}</div>
            <div className="url">
              {game.googlePlayPackageId ? (
                <>
                  <span className="google-play-mark">Google Play</span>
                  {game.googlePlayPackageId}
                </>
              ) : (
                'Internal feedback only'
              )}
            </div>
            <div className="foot">
              <span className="chip accent">
                {game.submissionEnabled ? 'Internal channel on' : 'Submissions off'}
              </span>
              <span className="faint">created {formatDate(game.createdAt)}</span>
            </div>
          </div>
        </div>
      </Link>

      <button
        aria-label={`Delete ${game.name}`}
        className="gc-del"
        title="Delete game"
        onClick={() => setConfirming(true)}
      >
        ✕
      </button>

      {confirming && (
        <div className="gc-confirm">
          <div>
            Delete <strong>{game.name}</strong> and all its feedback, analyses and imports?
          </div>
          {del.isError && <div className="faint">Delete failed — try again.</div>}
          <div className="inline">
            <button
              className="btn sm danger"
              disabled={del.isPending}
              onClick={() => del.mutate(game.id)}
            >
              {del.isPending ? 'Deleting…' : 'Delete'}
            </button>
            <button
              className="btn sm ghost"
              disabled={del.isPending}
              onClick={() => setConfirming(false)}
            >
              Cancel
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

export function SubmissionTokenCallout({
  gameId,
  gameName,
  submissionToken,
  onDismiss,
  defaultOpen = false,
}: {
  gameId: string;
  gameName: string;
  submissionToken: string;
  onDismiss?: () => void;
  defaultOpen?: boolean;
}) {
  const link = `${window.location.origin}/submit/${gameId}?token=${submissionToken}`;
  const [copied, setCopied] = useState(false);
  const [open, setOpen] = useState(defaultOpen);

  const copy = async (value: string) => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      /* clipboard blocked; the input is selectable as a fallback */
    }
  };

  return (
    <div className="callout">
      <div className="toolbar" style={{ alignItems: 'center' }}>
        <div className="head grow" style={{ marginBottom: 0 }}>
          ✓ Internal-feedback submit link for “{gameName}”
        </div>
        <button className="btn sm" onClick={() => setOpen((o) => !o)}>
          {open ? 'Hide' : 'Show submission link'}
        </button>
        {onDismiss && (
          <button className="btn sm ghost" onClick={onDismiss}>
            Dismiss
          </button>
        )}
      </div>
      <div className="faint" style={{ fontSize: 12, marginTop: 6 }}>
        One permanent link per game, separate from Google Play. It stays the same and can be
        copied again whenever you return.
      </div>

      {open && (
        <>
          <div className="faint" style={{ fontSize: 11.5, marginTop: 10 }}>PUBLIC SUBMIT LINK</div>
          <div className="copy-field">
            <input readOnly value={link} onFocus={(e) => e.target.select()} />
            <button className="btn sm" onClick={() => copy(link)}>
              {copied ? 'Copied!' : 'Copy'}
            </button>
          </div>
        </>
      )}
    </div>
  );
}
