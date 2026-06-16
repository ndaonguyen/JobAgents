import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev server on 5173. Proxy API + export to the JobAgents.Api backend (port 5300) so the React
// client uses same-origin relative URLs and avoids CORS during development.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5300', changeOrigin: true },
      '/export': { target: 'http://localhost:5300', changeOrigin: true },
    },
  },
});
