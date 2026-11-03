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
  build: {
    // redirect.html is the MSAL v5 redirect bridge page (ADR 0014 §3), built as a second entry.
    rolldownOptions: {
      input: {
        main: path.resolve(import.meta.dirname, 'index.html'),
        redirect: path.resolve(import.meta.dirname, 'redirect.html'),
      },
      output: {
        // Named, cache-friendly vendor chunks for what every page needs. Deliberately a short allow-list rather than
        // "everything in node_modules": MSAL (lazy, only with Entra config), zod and react-hook-form (only in the run
        // form panel) must stay out of the initial load. Without this, Rolldown names shared chunks after an arbitrary
        // module (e.g. "runTypePresentation" held React itself).
        advancedChunks: {
          groups: [
            { name: 'react-vendor', test: /node_modules[\\/](react|react-dom|scheduler)[\\/]/ },
            { name: 'router-vendor', test: /node_modules[\\/](react-router)[\\/]/ },
            { name: 'query-vendor', test: /node_modules[\\/](@tanstack|openapi-fetch|openapi-react-query)[\\/]/ },
            { name: 'i18n-vendor', test: /node_modules[\\/](i18next|react-i18next)[\\/]/ },
          ],
        },
      },
    },
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
    // Rendering the full route tree under parallel workers can be slow; the default 5 s is too tight for that.
    testTimeout: 15_000,
    hookTimeout: 30_000,
  },
})
