/// <reference types="vitest/config" />
import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Aspire sets PORT for the dev server and API_HTTPS / API_HTTP for the API's address (plan M1 §3.2).
// Without Aspire, fall back to the API's standalone launch profile.
const port = process.env.PORT ? Number(process.env.PORT) : undefined
const apiTarget = process.env.API_HTTPS ?? process.env.API_HTTP ?? 'https://localhost:7080'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, './src') },
  },
  server: {
    port: port ?? 5173,
    strictPort: port !== undefined,
    // The browser only ever calls relative /api; the API is never addressed directly (ADR 0004).
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true, secure: false },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    css: false,
  },
})
