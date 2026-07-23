import { useLayoutEffect, useMemo, useRef } from 'react';
import { useActivityFeed } from '../lib/useActivityFeed';
import type { ActivityEvent } from '../lib/types';

const LEVEL_CLASS: Record<string, string> = {
  run: 'lv-run',
  ok: 'lv-ok',
  warn: 'lv-warn',
  err: 'lv-err',
};
const LEVEL_TAG: Record<string, string> = { run: '▶', ok: '✓', warn: '!', err: '✕' };

function fmtTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime())
    ? '--:--:--'
    : d.toLocaleTimeString([], { hour12: false });
}

export function ActivityConsole({ gameId }: { gameId?: string }) {
  const { events, live } = useActivityFeed(true);
  const scrollRef = useRef<HTMLDivElement | null>(null);
  const stickRef = useRef(true);
  const visibleEvents = useMemo(
    () => events.filter((event) => !gameId || !event.gameId || event.gameId === gameId).slice(-40),
    [events, gameId],
  );

  // Track whether the user is pinned to the bottom.
  const onScroll = () => {
    const el = scrollRef.current;
    if (!el) return;
    stickRef.current = el.scrollHeight - el.scrollTop - el.clientHeight < 40;
  };
  useLayoutEffect(() => {
    const el = scrollRef.current;
    if (el && stickRef.current) el.scrollTop = el.scrollHeight;
  }, [visibleEvents]);

  return (
    <aside className="console-panel console-compact" aria-label="Live operations activity">
      <div className="console-head">
        <div>
          <div className="inline">
          <span className={`console-dot ${live ? 'on' : 'off'}`} />
            <strong>Live operations</strong>
          </div>
          <span className="console-sub">
            {live ? 'Watching this game in real time' : 'Connecting to worker stream…'}
          </span>
        </div>
        <span className={`console-live-badge ${live ? 'on' : ''}`}>{live ? 'LIVE' : 'WAIT'}</span>
      </div>

      <div className="ops-flow" aria-label="Processing stages">
        <span>Google Play</span>
        <i>→</i>
        <span>Queue</span>
        <i>→</i>
        <span>Qwen</span>
        <i>→</i>
        <span>Ready</span>
      </div>

      <div
        aria-live="polite"
        className="console"
        ref={scrollRef}
        onScroll={onScroll}
      >
        {visibleEvents.length === 0 ? (
          <div className="console-empty">
            <span className="idle-dot" />
            <strong>Workers are idle</strong>
            <span>New imports, analysis, retries, and summaries will appear here.</span>
          </div>
        ) : (
          visibleEvents.map((event, index) => <Line key={`${event.timestamp}-${index}`} e={event} />)
        )}
      </div>
      <div className="console-foot">
        <span>{visibleEvents.length} recent events</span>
        <span>Import · classify · summarize</span>
      </div>
    </aside>
  );
}

function Line({ e }: { e: ActivityEvent }) {
  const cls = LEVEL_CLASS[e.level] ?? 'lv-run';
  const tag = LEVEL_TAG[e.level] ?? '·';
  return (
    <div className="console-line">
      <span className="console-ts">{fmtTime(e.timestamp)}</span>
      <span className={`console-lv ${cls}`}>{tag}</span>
      <span className="console-msg">{e.message}</span>
    </div>
  );
}
