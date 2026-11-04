// Stand-in for the internal API, used only by the CI `deploy-artifacts` job to test the nginx web image (ADR 0029):
//   node scripts/stub-upstream.mjs [port]
// Every request is answered with JSON describing what the proxy delivered (method, URL, headers, body size). A
// `delay` query parameter (milliseconds) holds the response back, to check that nginx does not cut long calls.
import { createServer } from 'node:http'

const port = Number(process.argv[2] ?? 9100)

createServer((req, res) => {
  let bytes = 0
  req.on('data', (chunk) => {
    bytes += chunk.length
  })
  req.on('end', () => {
    const url = new URL(req.url ?? '/', 'http://stub')
    const delay = Number(url.searchParams.get('delay') ?? 0)
    setTimeout(() => {
      res.writeHead(200, {
        'content-type': 'application/json',
        // Headers a real API sends; the proxy must not duplicate the ones it sets itself.
        'cache-control': 'private, no-store',
        'x-content-type-options': 'nosniff',
        'x-stub': 'upstream',
      })
      res.end(JSON.stringify({ method: req.method, url: req.url, headers: req.headers, bytes }))
    }, delay)
  })
}).listen(port, '0.0.0.0', () => console.log(`stub upstream listening on ${port}`))
