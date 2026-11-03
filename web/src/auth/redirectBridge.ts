import { broadcastResponseToMainFrame } from '@azure/msal-browser/redirect-bridge'

// Entry of /redirect.html (ADR 0014 §3). For loginRedirect the bridge caches the response in sessionStorage and
// navigates back to the page that started the sign-in, where handleRedirectPromise picks it up.
broadcastResponseToMainFrame().catch((error: unknown) => {
  console.error('MSAL redirect bridge failed', error)
})
