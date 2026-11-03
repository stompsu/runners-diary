import { createFakeApi, type FakeApi, type FakeApiOptions } from './fakeApi'

let current: FakeApi = createFakeApi()

/** The fake API behind the global `fetch` stub for the current test (reset before every test). */
export function getFakeApi(): FakeApi {
  return current
}

/** Replaces the fake with one built from `options` (e.g. another `today`, an Entra config, tight limits). */
export function resetFakeApi(options?: FakeApiOptions): FakeApi {
  current = createFakeApi(options)
  return current
}
