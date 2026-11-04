#!/usr/bin/env bash
# Post-deploy smoke test (plan section 7.2): public URL only, read-mostly. Creates one guest per run (expires in 24 h).
#   infra/scripts/smoke.sh https://<web-fqdn>
# Exit code 0 = all checks passed. Needs curl and jq.
set -euo pipefail

base="${1:?usage: smoke.sh https://<web-fqdn>}"
base="${base%/}"
maps_display="${SMOKE_EXPECT_MAPS_TOKEN:-503}" # 503 unless Azure Maps display is on (plan G9), then 200
# SQL on the free offer resumes from pause in up to about a minute; the cold start of web + api adds more.
health_deadline="${SMOKE_HEALTH_TIMEOUT_SECONDS:-150}"

pass=0
fail() { echo "FAIL: $*" >&2; exit 1; }
ok() { pass=$((pass + 1)); echo "ok   $*"; }

headers="$(mktemp)"
trap 'rm -f "$headers"' EXIT

# 1. GET / -> 200 with a CSP header
code="$(curl -sS -o /dev/null -D "$headers" -w '%{http_code}' --max-time 90 "$base/")"
[ "$code" = "200" ] || fail "GET / returned $code"
grep -qi '^content-security-policy:' "$headers" || fail "GET / has no Content-Security-Policy header"
ok "GET / -> 200 with CSP"

# 2. /redirect.html -> 200, no COOP (the MSAL bridge breaks with one), no-store
code="$(curl -sS -o /dev/null -D "$headers" -w '%{http_code}' --max-time 30 "$base/redirect.html")"
[ "$code" = "200" ] || fail "GET /redirect.html returned $code"
if grep -qi '^cross-origin-opener-policy:' "$headers"; then fail "/redirect.html must not send Cross-Origin-Opener-Policy"; fi
grep -qi '^cache-control:.*no-store' "$headers" || fail "/redirect.html is not no-store"
ok "GET /redirect.html -> 200 without COOP, no-store"

# 3b. client-config (anonymous, no SQL)
code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 30 "$base/api/client-config")"
[ "$code" = "200" ] || fail "GET /api/client-config returned $code"
ok "GET /api/client-config -> 200"

# 3. guest session (cold start: web + api scale from zero; the guest creation does not need SQL warm for long)
body="$(mktemp)"
trap 'rm -f "$headers" "$body"' EXIT
start="$(date +%s)"
while :; do
  code="$(curl -sS -o "$body" -w '%{http_code}' --max-time 90 -X POST "$base/api/guest-sessions" || true)"
  [ "$code" = "201" ] && break
  [ $(( $(date +%s) - start )) -lt "$health_deadline" ] || fail "POST /api/guest-sessions still $code after ${health_deadline}s"
  sleep 5
done
token="$(jq -er '.accessToken' "$body")" || fail "guest session response has no accessToken"
echo "::add-mask::$token" 2>/dev/null || true
auth=(-H "Authorization: Bearer $token")
ok "POST /api/guest-sessions -> 201"

# Health: in production an anonymous /api/health runs only the live checks (no SQL, rate limited per IP); a request with a
# valid bearer token runs the full check including SQL. So the readiness check below uses the guest token.
code="$(curl -sS -o "$body" -w '%{http_code}' --max-time 90 "$base/api/health" || true)"
[ "$code" = "200" ] || fail "anonymous GET /api/health returned $code (expected the live-only answer, 200)"
ok "anonymous GET /api/health -> 200 (live checks only)"

# Full health (SQL) with the guest token; retries while the apps scale from zero and SQL resumes from pause.
start="$(date +%s)"
while :; do
  code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 60 "${auth[@]}" "$base/api/health" || true)"
  [ "$code" = "200" ] && break
  [ $(( $(date +%s) - start )) -lt "$health_deadline" ] || fail "authenticated GET /api/health (SQL) still $code after ${health_deadline}s"
  sleep 5
done
ok "authenticated GET /api/health (incl. SQL) -> 200 after $(( $(date +%s) - start ))s"

code="$(curl -sS -o "$body" -w '%{http_code}' --max-time 30 "${auth[@]}" "$base/api/me")"
[ "$code" = "200" ] || fail "GET /api/me returned $code"
zone="$(jq -er '.timeZoneId' "$body")"
[ "$zone" = "Europe/Warsaw" ] || fail "guest zone is '$zone', expected Europe/Warsaw (tzdata missing?)"
today="$(jq -er '.today' "$body")"
ok "GET /api/me -> zone $zone"

year="${today%%-*}"; rest="${today#*-}"; month="$((10#${rest%%-*}))"
code="$(curl -sS -o "$body" -w '%{http_code}' --max-time 30 "${auth[@]}" "$base/api/calendar/months/$year/$month")"
[ "$code" = "200" ] || fail "GET /api/calendar/months/$year/$month returned $code"
jq -e '(.runs | length) > 0' "$body" >/dev/null || fail "calendar of $year-$month has no runs (demo seed missing?)"
ok "GET /api/calendar/months/$year/$month -> 200 with runs"

# 5. maps token: 503 until Azure Maps display is switched on
code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 30 "${auth[@]}" "$base/api/maps/token")"
[ "$code" = "$maps_display" ] || fail "GET /api/maps/token returned $code, expected $maps_display"
ok "GET /api/maps/token -> $code"

echo "smoke: $pass checks passed"
