// Builds the map bench page that e2e/ checks and times. Not shipped.
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  // Production React drops Profiler callbacks; the profiling build keeps them at a small cost,
  // which both renderers pay equally.
  resolve: { alias: [{ find: /^react-dom\/client$/, replacement: 'react-dom/profiling' }] },
  build: {
    outDir: 'dist-bench',
    rollupOptions: { input: { map: 'bench/map.html' } },
  },
  preview: { port: 4173, strictPort: true },
});
