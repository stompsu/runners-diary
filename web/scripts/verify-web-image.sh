#!/usr/bin/env bash
# Black-box checks of the web image (ADR 0029 section 4, plan M7 AC3), run by the CI `deploy-artifacts` job:
#   scripts/verify-web-image.sh http://127.0.0.1:4173 [/assets/<worker>.mjs]
# The image must be running with API_UPSTREAM pointing at scripts/stub-upstream.mjs, CSP_ENTRA_HOST=https://e2etenant.ciamlogin.com
# and CSP_MAP_HOSTS=https://tiles.openfreemap.org (the defaults of the image). Needs curl and jq.
set -euo pipefail

base="${1:?usage: verify-web-image.sh <base url>}"
base="${base%/}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failures=0
fail() { echo "FAIL: $*" >&2; failures=$((failures + 1)); }
ok() { echo "ok   $*"; }

headers_of() { curl -sS -o /dev/null -D - --max-time 30 "$@"; }
header() { # header <name> from a dump on stdin: the first value, or empty
  grep -i "^$1:" | head -1 | cut -d: -f2- | sed 's/^ *//; s/\r$//'
}

# Start the slow request first so it runs while the rest is checked (nginx must not cut it at its 60 s default).
curl -sS -o "$work/slow.json" -w '%{http_code}' --max-time 130 "$base/api/slow?delay=90000" > "$work/slow.code" 2> "$work/slow.err" &
slow_pid=$!

# ---- headers on the SPA entry points ----
for path in / /diary/2026/11 /index.html; do
  h="$(headers_of "$base$path")"
  [ "$(printf '%s' "$h" | head -1 | awk '{print $2}')" = "200" ] || fail "GET $path is not 200"
  csp="$(printf '%s' "$h" | header content-security-policy)"
  [ -n "$csp" ] || fail "GET $path: no CSP"
  [ "$(printf '%s' "$h" | header cache-control)" = "no-store" ] || fail "GET $path: Cache-Control is not no-store"
  [ "$(printf '%s' "$h" | header cross-origin-opener-policy)" = "same-origin" ] || fail "GET $path: COOP is not same-origin"
  [ "$(printf '%s' "$h" | header x-content-type-options)" = "nosniff" ] || fail "GET $path: no nosniff"
  [ "$(printf '%s' "$h" | header strict-transport-security)" = "max-age=31536000" ] || fail "GET $path: HSTS differs"
  [ "$(printf '%s' "$h" | header referrer-policy)" = "strict-origin-when-cross-origin" ] || fail "GET $path: Referrer-Policy differs"
  printf '%s' "$h" | header permissions-policy | grep -q 'camera=()' || fail "GET $path: Permissions-Policy differs"
  printf '%s' "$h" | grep -qiE '^server: *nginx/' && fail "GET $path: server version is exposed"
done
ok "headers on /, /diary/2026/11 and /index.html (CSP, no-store, COOP, nosniff, HSTS, Referrer-Policy, Permissions-Policy)"

csp="$(headers_of "$base/" | header content-security-policy)"
for needle in "default-src 'self'" "script-src 'self';" "frame-ancestors 'none'" "worker-src 'self' blob:" \
  "https://tiles.openfreemap.org" "https://e2etenant.ciamlogin.com" "base-uri 'none'" "object-src 'none'"; do
  case "$csp" in *"$needle"*) ;; *) fail "CSP lacks: $needle" ;; esac
done
case "$csp" in *unsafe-eval* | *"script-src 'self' 'unsafe"*) fail "CSP allows unsafe script sources" ;; esac
case "$csp" in *"frame-src 'self' https://e2etenant.ciamlogin.com;"*) ;; *) fail "CSP frame-src is not 'self' + the Entra host (MSAL silent renewal)" ;; esac
ok "CSP is environment-templated (map host, Entra host) and strict on scripts; frame-src allows 'self' + Entra"

# ---- the MSAL redirect bridge: no COOP, no-store ----
h="$(headers_of "$base/redirect.html")"
[ -z "$(printf '%s' "$h" | header cross-origin-opener-policy)" ] || fail "/redirect.html must not send COOP"
[ "$(printf '%s' "$h" | header cache-control)" = "no-store" ] || fail "/redirect.html is not no-store"
redirect_csp="$(printf '%s' "$h" | header content-security-policy)"
[ -n "$redirect_csp" ] || fail "/redirect.html has no CSP"
# The MSAL silent-renewal iframe loads /redirect.html from our own origin: only that page may be framed, and only by itself.
case "$redirect_csp" in *"frame-ancestors 'self';"*) ;; *) fail "/redirect.html must have frame-ancestors 'self'" ;; esac
for path in / /index.html /diary/2026/11 /api/x /robots.txt; do
  case "$(headers_of "$base$path" | header content-security-policy)" in *"frame-ancestors 'none';"*) ;; *) fail "GET $path must keep frame-ancestors 'none'" ;; esac
done
ok "/redirect.html: no COOP, no-store, CSP with frame-ancestors 'self'; every other path keeps frame-ancestors 'none'"

# ---- assets ----
asset="$(curl -sS "$base/index.html" | grep -o '/assets/[^"]*\.js' | head -1)"
h="$(headers_of "$base$asset")"
[ "$(printf '%s' "$h" | header cache-control)" = "public, max-age=31536000, immutable" ] || fail "$asset is not immutable"
ok "hashed asset $asset is immutable"
code="$(curl -sS -o /dev/null -w '%{http_code}' "$base/assets/missing.js")"
[ "$code" = "404" ] || fail "a missing asset answered $code instead of 404"
h="$(headers_of "$base/assets/missing.js")"
printf '%s' "$h" | header cache-control | grep -qi immutable && fail "a 404 for a missing asset is cached as immutable"
ok "missing asset: 404 (no SPA fallback, not cached)"
# Optional 2nd argument: path of a built .mjs file (MapLibre's module worker), e.g. /assets/maplibre-gl-worker-XXXX.mjs.
# With nosniff, a wrong MIME type makes the browser refuse the worker, which broke the map once (ADR 0029).
if [ -n "${2:-}" ]; then
  [ "$(headers_of "$base$2" | header content-type)" = "text/javascript" ] || fail "$2 is not served as text/javascript"
  ok "module worker $2 is text/javascript"
fi
[ "$(headers_of "$base/robots.txt" | head -1 | awk '{print $2}')" = "200" ] || fail "robots.txt missing"
curl -sS "$base/robots.txt" | grep -q '^Disallow: /$' || fail "robots.txt does not disallow everything"
ok "robots.txt disallows everything"
[ "$(curl -sS "$base/nginx-alive")" = "ok" ] || fail "/nginx-alive is not ok"

# ---- /api proxy ----
res="$(curl -sS -H 'X-Forwarded-For: 1.1.1.1, 2.2.2.2' -H 'X-Forwarded-For: 9.9.9.9' -H 'X-Forwarded-Proto: https' "$base/api/x?a=1")"
[ "$(echo "$res" | jq -r .url)" = "/api/x?a=1" ] || fail "the API path/query is not passed unchanged: $(echo "$res" | jq -r .url)"
[ "$(echo "$res" | jq -r '.headers["x-forwarded-for"]')" = "9.9.9.9" ] || fail "X-Forwarded-For was not reduced to the rightmost value: $(echo "$res" | jq -r '.headers["x-forwarded-for"]')"
[ "$(echo "$res" | jq -r '.headers["x-forwarded-proto"]')" = "https" ] || fail "X-Forwarded-Proto not passed"
ok "/api reaches the upstream; a spoofed X-Forwarded-For chain is reduced to the rightmost address"

h="$(headers_of "$base/api/x")"
[ "$(printf '%s' "$h" | header cache-control)" = "private, no-store" ] || fail "the API's Cache-Control does not pass through"
[ "$(printf '%s' "$h" | grep -ic '^x-content-type-options:')" = "1" ] || fail "X-Content-Type-Options is duplicated on /api"
ok "/api responses keep the API's Cache-Control and carry each security header once"

# ---- bodies ----
head -c 21000000 /dev/zero > "$work/21mb"
res="$(curl -sS --max-time 120 -X POST --data-binary @"$work/21mb" "$base/api/x")"
[ "$(echo "$res" | jq -r .bytes)" = "21000000" ] || fail "a 21 MB body did not reach the upstream"
res="$(curl -sS --max-time 120 -X POST --data-binary @"$work/21mb" "$base/api/runs/3fa85f64-5717-4562-b3fc-2c963f66afa6/tracks")"
[ "$(echo "$res" | jq -r .bytes)" = "21000000" ] || fail "a 21 MB track upload did not reach the upstream"
ok "21 MB bodies pass (generic /api and track upload)"
head -c 23000000 /dev/zero > "$work/23mb"
code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 120 -X POST --data-binary @"$work/23mb" "$base/api/x")"
[ "$code" = "413" ] || fail "a 23 MB body answered $code instead of 413"
ok "a 23 MB body is refused with 413"

# ---- long calls ----
if wait "$slow_pid" && [ "$(cat "$work/slow.code")" = "200" ]; then
  ok "a 90 s upstream response is not cut (proxy_read_timeout 120 s)"
else
  fail "the 90 s delayed response was cut: code $(cat "$work/slow.code" 2>/dev/null) $(cat "$work/slow.err" 2>/dev/null)"
fi

if [ "$failures" -gt 0 ]; then echo "$failures check(s) failed" >&2; exit 1; fi
echo "web image: all checks passed"
