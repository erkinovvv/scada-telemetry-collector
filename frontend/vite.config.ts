import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// В разработке запросы /api и /health проксируются на локальный API (dotnet run → :5080).
// В Docker ту же роль выполняет nginx (frontend/nginx.conf).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': process.env.API_URL ?? 'http://localhost:5080',
      '/health': process.env.API_URL ?? 'http://localhost:5080',
    },
  },
});
