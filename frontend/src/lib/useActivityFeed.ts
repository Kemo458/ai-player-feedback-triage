import { useEffect, useRef, useState } from 'react';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { api } from './api';
import { getToken } from './auth';
import type { ActivityEvent } from './types';

const MAX = 400;

/**
 * Live worker activity feed. Backfills from GET /api/activity, then streams new events
 * from the SignalR `activity` broadcast (global — every worker action across all games).
 */
export function useActivityFeed(enabled: boolean): {
  events: ActivityEvent[];
  live: boolean;
} {
  const [events, setEvents] = useState<ActivityEvent[]>([]);
  const [live, setLive] = useState(false);
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    if (!enabled) return;
    let disposed = false;

    // Backfill recent history.
    api
      .get<ActivityEvent[]>('/api/activity?limit=200')
      .then((initial) => {
        if (!disposed) setEvents(initial.slice(-MAX));
      })
      .catch(() => {
        /* non-fatal; stream will still populate */
      });

    const connection = new HubConnectionBuilder()
      .withUrl(`/hubs/feedback?access_token=${encodeURIComponent(getToken() ?? '')}`)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 15000])
      .configureLogging(LogLevel.Warning)
      .build();
    connectionRef.current = connection;

    connection.on('activity', (e: ActivityEvent) => {
      setEvents((prev) => {
        const next = prev.length >= MAX ? prev.slice(prev.length - MAX + 1) : prev;
        return [...next, e];
      });
    });
    connection.onreconnecting(() => setLive(false));
    connection.onreconnected(() => setLive(true));
    connection.onclose(() => setLive(false));

    connection
      .start()
      .then(() => {
        if (!disposed) setLive(true);
      })
      .catch(() => setLive(false));

    return () => {
      disposed = true;
      connection.off('activity');
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop();
      connectionRef.current = null;
    };
  }, [enabled]);

  return { events, live };
}
