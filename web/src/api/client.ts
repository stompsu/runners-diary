import createFetchClient from 'openapi-fetch'
import createClient from 'openapi-react-query'
import type { paths } from './generated/schema'

/**
 * The only way the app talks to the API (CLAUDE.md). Requests always go to the page's own origin
 * (`/api/...`), so the same build works behind the Vite proxy and behind the production reverse proxy.
 * The origin is spelled out because `Request` needs an absolute URL outside browsers (tests).
 * M2 adds an auth middleware here (`fetchClient.use(...)`).
 */
export const fetchClient = createFetchClient<paths>({ baseUrl: window.location.origin })

export const $api = createClient(fetchClient)
