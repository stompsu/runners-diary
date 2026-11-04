/// <reference types="node" />
import '@testing-library/jest-dom/vitest'
import { Blob as NodeBlob, File as NodeFile } from 'node:buffer'
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

// jsdom's FormData / Blob / File cannot be sent by Node's fetch: undici does not recognise them, so an uploaded file
// would arrive as the text "[object File]" in a part called "blob". Node's own classes (FormData is the one behind
// `Response.formData()`) are serialised properly. Nothing else in the app creates files or form data, and the
// download test only needs a Blob, which Node's `Response.blob()` returns.
const nodeFormData = (
  await new Response('a=1', { headers: { 'content-type': 'application/x-www-form-urlencoded' } }).formData()
).constructor as typeof FormData
// The Proxy returns an EMPTY FormData in exactly one case: the first constructor argument is a jsdom
// HTMLFormElement. React 19 builds `new FormData(form, submitter)` itself when a submit handler starts a transition
// (react-router navigations do) and ignores the result there; Node's class would throw on a jsdom form. Consequence:
// a test (or app code) that reads fields via `new FormData(formElement)` gets NO entries under test. Read form values
// through react-hook-form / controlled state or from the event instead; every other call is passed through unchanged.
vi.stubGlobal(
  'FormData',
  new Proxy(nodeFormData, {
    construct: (target, args: unknown[]) =>
      args[0] instanceof HTMLFormElement ? new target() : Reflect.construct(target, args),
  }),
)
vi.stubGlobal('Blob', NodeBlob)
vi.stubGlobal('File', NodeFile)

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
