import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, beforeEach, vi } from 'vitest'
import '@/i18n'
import { jsonResponse } from './api'

// openapi-fetch captures `fetch` when the client module loads, so the stub must exist before any test imports it.
// Tests change behaviour per case with `vi.mocked(fetch).mockResolvedValueOnce(...)`.
vi.stubGlobal('fetch', vi.fn())

beforeEach(() => {
  vi.mocked(fetch).mockReset()
  vi.mocked(fetch).mockImplementation(() => Promise.resolve(jsonResponse({ status: 'Healthy', version: 'test' })))
})

afterEach(() => cleanup())
