import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev proxy: forward API + SignalR hub to the local Caddy on :8090.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:8090',
        changeOrigin: true,
      },
      '/hubs': {
        target: 'http://localhost:8090',
        changeOrigin: true,
        ws: true,
      },
      '/health': {
        target: 'http://localhost:8090',
        changeOrigin: true,
      },
    },
  },
});
