#!/usr/bin/env bash
# Shared helpers for the end-to-end test scripts.

ID=${SAMA_IDENTITY:-http://127.0.0.1:8081}
CT=${SAMA_CATALOG:-http://127.0.0.1:8082}
BK=${SAMA_BASKET:-http://127.0.0.1:8083}
OR=${SAMA_ORDERING:-http://127.0.0.1:8084}
IN=${SAMA_INVENTORY:-http://127.0.0.1:8086}
PY_=${SAMA_PAYMENT:-http://127.0.0.1:8087}
GW=${SAMA_GATEWAY:-http://127.0.0.1:8080}
PW=${SAMA_TEST_PASSWORD:-CorrectHorse123Battery}

FAILED=0
pass() { printf '  \033[32mPASS\033[0m %s\n' "$1"; }
fail() { printf '  \033[31mFAIL\033[0m %s -- %s\n' "$1" "$2"; FAILED=1; }

# Reads a dotted path out of a JSON document on stdin, printing '' when absent.
J() {
  python3 -c "
import json,sys
try: d=json.loads(sys.stdin.read() or '{}')
except Exception: print(''); sys.exit()
v=d
for k in sys.argv[1].split('.'):
    if v is None: break
    v = v[int(k)] if k.isdigit() and isinstance(v,list) else (v.get(k) if isinstance(v,dict) else None)
print('' if v is None else v)" "$1"
}

# Clears rate-limit counters so a suite is not throttled by the one before it.
reset_limits() { docker exec sama-redis redis-cli FLUSHALL >/dev/null 2>&1 || true; }

# Registers an account and echoes its access token.
new_user() {
  local email="$1" name="${2:-Test User}"
  curl -s -X POST "$ID/api/v1/auth/register" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$email\",\"password\":\"$PW\",\"displayName\":\"$name\"}" | J accessToken
}

# Registers an account, grants it the administrator role, and echoes a fresh token that
# reflects the grant. Roles are baked into the token at issue time, so re-authenticating is
# required rather than optional.
new_admin() {
  local email="admin+$(date +%s%N)@sama.test"
  local body uid
  body=$(curl -s -X POST "$ID/api/v1/auth/register" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$email\",\"password\":\"$PW\",\"displayName\":\"Test Admin\"}")
  uid=$(python3 -c "
import json,base64,sys
t=json.loads(sys.argv[1])['accessToken'].split('.')[1]; t+='='*(-len(t)%4)
print(json.loads(base64.urlsafe_b64decode(t))['sub'])" "$body")
  docker exec sama-postgres psql -U sama -d sama -q -c \
    "INSERT INTO identity.user_roles(user_id, role) VALUES ('$uid','administrator') ON CONFLICT DO NOTHING;" >/dev/null 2>&1
  curl -s -X POST "$ID/api/v1/auth/login" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$email\",\"password\":\"$PW\"}" | J accessToken
}
