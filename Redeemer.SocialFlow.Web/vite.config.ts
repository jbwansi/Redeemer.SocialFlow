import { loadEnv } from 'vite'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  return {
    plugins: [react()],
    server: {
      port: 5173,
      strictPort: true,
      proxy: {
        '/api': { target: env.API_PROXY_TARGET || 'https://127.0.0.1:65474', changeOrigin: true,secure:false },
      },
    },
    test: { environment: 'jsdom', setupFiles: './src/test/setup.ts', restoreMocks: true },
  }
})
