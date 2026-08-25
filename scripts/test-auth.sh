#!/usr/bin/env bash
# Authentication: registration, sign-in, permission enforcement, refresh-token rotation and
# theft detection, login throttling, response hardening.
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")" && . ./lib.sh

reset_limits
EMAIL="auth+$(date +%s%N)@sama.test"

echo "1) registration"
R=$(curl -s -w '\n%{http_code}' -X POST "$ID/api/v1/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PW\",\"displayName\":\"Auth Test\"}")
CODE=$(tail -1 <<<"$R"); BODY=$(sed '$d' <<<"$R")
[ "$CODE" = "201" ] && pass "register -> 201" || fail "register" "$CODE $BODY"
AT=$(J accessToken <<<"$BODY"); RT=$(J refreshToken <<<"$BODY")
[ -n "$AT" ] && pass "access token issued" || fail "access token" "empty"

echo "2) refresh token is also set as an HttpOnly cookie"
H=$(curl -s -D- -o /dev/null -X POST "$ID/api/v1/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PW\"}" | tr -d '\r')
grep -qi "^set-cookie: sama_rt=.*httponly" <<<"$H" && pass "HttpOnly cookie set" || fail "cookie" "$(grep -i set-cookie <<<"$H")"
grep -qi "samesite=strict" <<<"$H" && pass "SameSite=Strict" || fail "samesite" "missing"

echo "3) password policy"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$ID/api/v1/auth/register" -H 'Content-Type: application/json' \
  -d '{"email":"weak@sama.test","password":"short1","displayName":"W"}')
[ "$CODE" = "400" ] && pass "weak password -> 400" || fail "password policy" "$CODE"

echo "4) authenticated profile"
R=$(curl -s -w '\n%{http_code}' "$ID/api/v1/auth/me" -H "Authorization: Bearer $AT")
CODE=$(tail -1 <<<"$R"); BODY=$(sed '$d' <<<"$R")
[ "$CODE" = "200" ] && pass "me -> 200" || fail "me" "$CODE"
echo "     permissions: $(python3 -c "import json,sys;print(', '.join(json.loads(sys.argv[1])['permissions'][:3])+' …')" "$BODY" 2>/dev/null)"
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$ID/api/v1/auth/me")
[ "$CODE" = "401" ] && pass "anonymous -> 401" || fail "anonymous me" "$CODE"

echo "5) permission enforcement"
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$ID/api/v1/users" -H "Authorization: Bearer $AT")
[ "$CODE" = "403" ] && pass "customer -> /users 403" || fail "permission guard" "$CODE"

echo "6) refresh-token rotation"
R=$(curl -s -w '\n%{http_code}' -X POST "$ID/api/v1/auth/refresh" -H 'Content-Type: application/json' \
  -d "{\"refreshToken\":\"$RT\"}")
CODE=$(tail -1 <<<"$R"); BODY=$(sed '$d' <<<"$R"); RT2=$(J refreshToken <<<"$BODY")
[ "$CODE" = "200" ] && pass "refresh -> 200" || fail "refresh" "$CODE $BODY"
{ [ -n "$RT2" ] && [ "$RT2" != "$RT" ]; } && pass "token rotated" || fail "rotation" "unchanged"

echo "7) reuse of a consumed token is treated as theft"
R=$(curl -s -w '\n%{http_code}' -X POST "$ID/api/v1/auth/refresh" -H 'Content-Type: application/json' \
  -d "{\"refreshToken\":\"$RT\"}")
CODE=$(tail -1 <<<"$R"); BODY=$(sed '$d' <<<"$R")
[ "$CODE" = "401" ] && pass "replayed token -> 401" || fail "reuse detection" "$CODE"
grep -q "auth.refresh.reused" <<<"$BODY" && pass "reported as reuse" || fail "reuse code" "$BODY"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$ID/api/v1/auth/refresh" -H 'Content-Type: application/json' \
  -d "{\"refreshToken\":\"$RT2\"}")
[ "$CODE" = "401" ] && pass "whole family revoked" || fail "family revocation" "$CODE"

echo "8) wrong password"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$ID/api/v1/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"WrongPassword123\"}")
[ "$CODE" = "401" ] && pass "wrong password -> 401" || fail "wrong password" "$CODE"

echo "9) login throttling (5 per 5 min, per IP and username)"
LAST=""
for i in $(seq 1 8); do
  LAST=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$ID/api/v1/auth/login" \
    -H 'Content-Type: application/json' -d "{\"email\":\"rl-$EMAIL\",\"password\":\"nope\"}")
done
[ "$LAST" = "429" ] && pass "throttled -> 429" || fail "rate limit" "last=$LAST"
H=$(curl -s -D- -o /dev/null -X POST "$ID/api/v1/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"rl-$EMAIL\",\"password\":\"nope\"}" | grep -iE '^(retry-after|ratelimit-)' | tr -d '\r')
[ -n "$H" ] && pass "advisory headers present" || fail "headers" "none"
sed 's/^/     /' <<<"$H"

echo "10) response hardening"
H=$(curl -s -D- -o /dev/null "$ID/health/live" | tr -d '\r')
for h in x-content-type-options x-frame-options content-security-policy referrer-policy; do
  grep -qi "^$h:" <<<"$H" && pass "$h" || fail "$h" "missing"
done
grep -qi "^server:" <<<"$H" && fail "Server header leaked" "$(grep -i '^server:' <<<"$H")" || pass "no Server header"

exit $FAILED
