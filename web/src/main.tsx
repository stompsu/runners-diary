import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBrowserRouter, RouterProvider } from 'react-router'
import { Providers } from '@/app/providers'
import { routes } from '@/app/routes'
import { initLanguage } from '@/i18n/language'
import './index.css'

const router = createBrowserRouter(routes)

const root = document.getElementById('root')
if (!root) throw new Error('Root element #root not found')

/**
 * The language (and its eager bundle) is settled before the first render, so there is no flash of the wrong one. This
 * runs in the entry module because the CSP forbids inline scripts (ADR 0042 §2). A failure keeps English. A function
 * rather than top-level await: TLA in the entry changes Rolldown's chunking and costs ~7 kB gzip of initial load.
 */
async function start(container: HTMLElement) {
  await initLanguage().catch(() => undefined)
  createRoot(container).render(
    <StrictMode>
      <Providers>
        <RouterProvider router={router} />
      </Providers>
    </StrictMode>,
  )
}

void start(root)
