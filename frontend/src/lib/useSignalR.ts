import { useEffect, useRef, useState } from 'react';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { getToken } from './auth';
import { queryKeys } from './queryKeys';
import { useToasts } from '../components/Toasts';

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

interface NotifyPayload {
  eventType: 'FeedbackCompleted' | 'ImportProgressChanged' | 'SummaryUpdated';
  gameId: string;
  feedbackId?: string;
  importId?: string;
  summaryId?: string;
}

// Connect to the feedback hub, join the game group, and invalidate query keys
// on `notify` events. SignalR problems must NOT block REST use — this only drives
// a small status indicator.
export function useSignalR(gameId: string | undefined): ConnectionStatus {
  const qc = useQueryClient();
  const { push } = useToasts();
  const [status, setStatus] = useState<ConnectionStatus>('connecting');
  const connectionRef = useRef<HubConnection | null>(null);
  const lastFeedbackToast = useRef(0);

  useEffect(() => {
    if (!gameId) return;

    const connection = new HubConnectionBuilder()
      .withUrl(`/hubs/feedback?access_token=${encodeURIComponent(getToken() ?? '')}`)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 15000])
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;
    let disposed = false;

    const invalidateGame = () => {
      qc.invalidateQueries({ queryKey: ['dashboard', gameId] });
      qc.invalidateQueries({ queryKey: ['pipeline', gameId] });
      qc.invalidateQueries({ queryKey: ['summary', gameId] });
      qc.invalidateQueries({ queryKey: ['feedback', gameId] });
      qc.invalidateQueries({ queryKey: ['entities', gameId] });
    };

    connection.on('notify', (payload: NotifyPayload) => {
      if (!payload || payload.gameId !== gameId) return;
      switch (payload.eventType) {
        case 'FeedbackCompleted':
          qc.invalidateQueries({ queryKey: ['feedback', gameId] });
          qc.invalidateQueries({ queryKey: ['dashboard', gameId] });
          qc.invalidateQueries({ queryKey: ['pipeline', gameId] });
          qc.invalidateQueries({ queryKey: ['entities', gameId] });
          if (Date.now() - lastFeedbackToast.current > 3000) {
            lastFeedbackToast.current = Date.now();
            push('New feedback analysis available.', 'success');
          }
          break;
        case 'ImportProgressChanged':
          if (payload.importId) {
            qc.invalidateQueries({ queryKey: queryKeys.import(payload.importId) });
          }
          qc.invalidateQueries({ queryKey: ['dashboard', gameId] });
          qc.invalidateQueries({ queryKey: ['pipeline', gameId] });
          qc.invalidateQueries({ queryKey: ['feedback', gameId] });
          break;
        case 'SummaryUpdated':
          qc.invalidateQueries({ queryKey: ['summary', gameId] });
          push('AI summary updated.', 'success');
          break;
        default:
          invalidateGame();
      }
    });

    connection.onreconnecting(() => setStatus('reconnecting'));
    connection.onreconnected(async () => {
      setStatus('connected');
      // Rejoin the group and refetch everything after a reconnect.
      try {
        await connection.invoke('JoinGame', gameId);
      } catch {
        /* transient; ignore */
      }
      invalidateGame();
    });
    connection.onclose(() => {
      if (!disposed) setStatus('disconnected');
    });

    const start = async () => {
      try {
        setStatus('connecting');
        await connection.start();
        if (disposed) return;
        await connection.invoke('JoinGame', gameId);
        setStatus('connected');
      } catch {
        if (!disposed) setStatus('disconnected');
      }
    };
    void start();

    return () => {
      disposed = true;
      connection.off('notify');
      if (connection.state !== HubConnectionState.Disconnected) {
        void connection.stop();
      }
      connectionRef.current = null;
    };
  }, [gameId, push, qc]);

  return status;
}
