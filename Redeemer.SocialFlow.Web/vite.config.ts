import { visualGateway } from './server/visualGateway'
import { loadEnv } from 'vite'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { readFileSync } from 'node:fs'

export default defineConfig(({ mode, command }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const serving = command === 'serve' && !process.env.VITEST
  if (serving && (!process.env.SOCIALFLOW_HTTPS_CERT || !process.env.SOCIALFLOW_HTTPS_KEY))
    throw new Error('HTTPS certificate required. Start with powershell -File ../scripts/Start-Frontend.ps1')
  return {
    plugins: [
      react(),
      visualGateway(
        mode === 'development' && process.env.SOCIALFLOW_VISUAL_GATEWAY_ENABLED === 'true',
        process.env.SOCIALFLOW_VISUAL_ADMIN_SECRET,
      ),
    ],
    server: {
      host: 'localhost',
      https: serving ? {
        cert: readFileSync(process.env.SOCIALFLOW_HTTPS_CERT!),
        key: readFileSync(process.env.SOCIALFLOW_HTTPS_KEY!),
      } : undefined,
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
