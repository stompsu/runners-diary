import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { afterEach, beforeEach, vi } from 'vitest'
import '@/i18n'
import { resetSessionForTests } from '@/auth/session'
import { getFakeApi, resetFakeApi } from './fake'
import { installMatchMedia } from './viewport'

// openapi-fetch captures `fetch` when the client module loads, so the stub must exist before any test imports it.
// By default every request is answered by the in-memory fake API (ADR 0019). Tests override single responses with
// `vi.mocked(fetch).mockResolvedValueOnce(...)`, or reconfigure the fake with `resetFakeApi({...})` / `getFakeApi()`.
// One deliberate place for async-util patience: CI runners and parallel workers are slower than a dev laptop.
configure({ asyncUtilTimeout: 4000 })

vi.stubGlobal('fetch', vi.fn())

beforeEach(() => {
  installMatchMedia()
  localStorage.clear()
  sessionStorage.clear()
  resetSessionForTests()
  resetFakeApi()
  vi.mocked(fetch).mockReset()
  vi.mocked(fetch).mockImplementation((input, init) =>
    getFakeApi().handle(input instanceof Request ? input : new Request(input, init)),
  )
})

afterEach(() => cleanup())
