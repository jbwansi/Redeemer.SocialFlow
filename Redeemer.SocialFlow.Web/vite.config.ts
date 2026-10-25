import { visualGateway } from './server/visualGateway'
import { loadEnv } from 'vite'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  return {
    plugins: [
      react(),
      visualGateway(
        mode === 'development' && process.env.SOCIALFLOW_VISUAL_GATEWAY_ENABLED === 'true',
        process.env.SOCIALFLOW_VISUAL_ADMIN_SECRET,
      ),
    ],
    server: {
      host: '127.0.0.1',
      port: 5173,
      strictPort: true,
      proxy: {
        '/api': {
          target: env.API_PROXY_TARGET || 'https://localhost:65474',
          changeOrigin: true,
          secure: true,
        },
      },
    },
    test: { environment: 'jsdom', setupFiles: './src/test/setup.ts', restoreMocks: true },
  }
})
