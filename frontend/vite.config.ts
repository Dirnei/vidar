import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: { proxy: { '/api': 'http://localhost:5280' } },
  build: {
    outDir: '../src/Vidar.Host/wwwroot',
    emptyOutDir: true,
    rollupOptions: {
      output: {
        // Split the rarely-changing framework code into its own long-cached chunk
        // so app updates don't force clients to re-download React on every deploy.
        manualChunks(id) {
          if (id.includes('node_modules')) return 'react-vendor';
        },
      },
    },
  },
})
