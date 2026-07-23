import type { ConnectionStatus } from '../lib/useSignalR';

const LABELS: Record<ConnectionStatus, string> = {
  connecting: 'Connecting…',
  connected: 'Live',
  reconnecting: 'Reconnecting…',
  disconnected: 'Offline',
};

// Small non-blocking realtime indicator. SignalR issues never block REST use.
export function ConnectionPill({ status }: { status: ConnectionStatus }) {
  return (
    <span className={`conn-pill conn-${status}`} title="Realtime updates">
      <span className="dot" />
      {LABELS[status]}
    </span>
  );
}
