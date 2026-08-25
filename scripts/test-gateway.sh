#!/usr/bin/env bash
# Everything through the single edge on :8080 — no direct service ports.
set -uo pipefail
GW=http://127.0.0.1:8080
PW="CorrectHorse123Battery"
FAILED=0
pass(){ printf '  \033[32mPASS\033[0m %s\n' "$1"; }
fail(){ printf '  \033[31mFAIL\033[0m %s -- %s\n' "$1" "$2"; FAILED=1; }
J(){ python3 -c "
import json,sys
try: d=json.loads(sys.stdin.read() or '{}')
except Exception: print(''); sys.exit()
v=d
for k in sys.argv[1].split('.'):
    if v is None: break
    v = v[int(k)] if k.isdigit() and isinstance(v,list) else (v.get(k) if isinstance(v,dict) else None)
print('' if v is None else v)" "$1"; }

docker exec sama-redis redis-cli FLUSHALL >/dev/null 2>&1

echo "1) routing to every backend"
for path in "/.well-known/jwks.json" "/api/v1/products" "/api/v1/categories"; do
  CODE=$(curl -s -o /dev/null -w '%{http_code}' "$GW$path")
  [ "$CODE" = "200" ] && pass "GET $path -> 200" || fail "route $path" "$CODE"
done

echo "2) auth through the gateway"
EMAIL="gw+$(date +%s%N)@sama.test"
BODY=$(curl -s -X POST "$GW/api/v1/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PW\",\"displayName\":\"GW User\"}")
AT=$(J accessToken <<<"$BODY")
[ -n "$AT" ] && pass "register via gateway" || fail "register" "$BODY"
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$GW/api/v1/auth/me" -H "Authorization: Bearer $AT")
[ "$CODE" = "200" ] && pass "authenticated proxy -> 200" || fail "me" "$CODE"

echo "3) edge rejects a bad token before reaching a backend"
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$GW/api/v1/basket" -H "Authorization: Bearer not.a.real.token")
[ "$CODE" = "401" ] && pass "forged token -> 401" || fail "token validation" "$CODE"

echo "4) internal-only endpoint is not reachable from outside"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$GW/api/v1/products/lookup" \
  -H "Authorization: Bearer $AT" -H 'Content-Type: application/json' -d '{"productIds":[]}')
[ "$CODE" != "200" ] && pass "internal lookup blocked at edge ($CODE)" || fail "lookup exposed" "200"

echo "5) protected routes require auth"
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$GW/api/v1/basket")
[ "$CODE" = "401" ] && pass "anonymous basket -> 401" || fail "anon basket" "$CODE"
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$GW/api/v1/orders")
[ "$CODE" = "401" ] && pass "anonymous orders -> 401" || fail "anon orders" "$CODE"

echo "7) security headers on proxied responses"
H=$(curl -s -D- -o /dev/null "$GW/api/v1/products" | tr -d '\r')
for h in x-content-type-options x-frame-options content-security-policy; do
  grep -qi "^$h:" <<<"$H" && pass "$h" || fail "$h" "missing"
done
grep -qi "^server:" <<<"$H" && fail "server header leaked" "$(grep -i '^server:' <<<"$H")" || pass "no Server header"

echo "8) checkout end-to-end through the gateway only"
# Provision a dedicated product: picking an arbitrary one from the catalogue can select the
# fixture priced to trigger a simulated decline, which would fail this test for the right reason.
ADMIN_EMAIL="gw-admin+$(date +%s%N)@sama.test"
AB=$(curl -s -X POST "$GW/api/v1/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$PW\",\"displayName\":\"GW Admin\"}")
AUID=$(python3 -c "
import json,base64,sys
t=json.loads(sys.argv[1])['accessToken'].split('.')[1]; t+='='*(-len(t)%4)
print(json.loads(base64.urlsafe_b64decode(t))['sub'])" "$AB")
docker exec sama-postgres psql -U sama -d sama -q -c \
  "INSERT INTO identity.user_roles(user_id,role) VALUES ('$AUID','administrator') ON CONFLICT DO NOTHING;" >/dev/null 2>&1
ADMIN=$(curl -s -X POST "$GW/api/v1/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$PW\"}" | J accessToken)
GCAT=$(curl -s -X POST "$GW/api/v1/categories" -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d "{\"name\":\"Gateway Goods\",\"slug\":\"gateway-goods-$(date +%s)\",\"sortOrder\":1}" | J id)
GSKU="GW-$(date +%s)"
PID=$(curl -s -X POST "$GW/api/v1/products" -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d "{\"sku\":\"$GSKU\",\"name\":\"Gateway Widget\",\"slug\":\"gateway-widget-$(date +%s)\",\"description\":\"Checkout fixture routed entirely through the edge.\",\"categoryId\":\"$GCAT\",\"price\":42.00,\"currency\":\"USD\",\"images\":[{\"url\":\"/media/g.jpg\",\"altText\":\"g\",\"sortOrder\":0}]}" | J id)
curl -s -o /dev/null -X POST "$GW/api/v1/products/$PID/publish" -H "Authorization: Bearer $ADMIN"
curl -s -o /dev/null -X POST "$GW/api/v1/stock" -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d "{\"productId\":\"$PID\",\"sku\":\"$GSKU\",\"quantityOnHand\":25,\"reorderLevel\":1}"
if [ -n "$PID" ]; then
  curl -s -o /dev/null -X POST "$GW/api/v1/basket/items" -H "Authorization: Bearer $AT" \
    -H 'Content-Type: application/json' -d "{\"productId\":\"$PID\",\"quantity\":1}"
  Q=$(curl -s "$GW/api/v1/basket" -H "Authorization: Bearer $AT" | J totalQuantity)
  [ "$Q" = "1" ] && pass "basket via gateway (qty=$Q)" || fail "basket" "$Q"
  R=$(curl -s -w '\n%{http_code}' -X POST "$GW/api/v1/orders" -H "Authorization: Bearer $AT" \
    -H 'Content-Type: application/json' -H "Idempotency-Key: gw-$(date +%s%N)" \
    -d '{"shippingAddress":{"line1":"1 Nile St","city":"Cairo","postcode":"11511","country":"EG"}}')
  CODE=$(tail -1 <<<"$R"); OID=$(sed '$d' <<<"$R" | J id)
  [ "$CODE" = "202" ] && pass "checkout via gateway -> 202" || fail "checkout" "$CODE"
  ST=""
  for _ in $(seq 1 40); do
    ST=$(curl -s "$GW/api/v1/orders/$OID" -H "Authorization: Bearer $AT" | J status)
    [ "$ST" = "Confirmed" ] || [ "$ST" = "Cancelled" ] && break
    sleep 0.75
  done
  [ "$ST" = "Confirmed" ] && pass "saga completed through the gateway" || fail "saga" "$ST"
else
  fail "checkout setup" "no product available"
fi

echo "9) edge rate limiting (search: 30/min)"
LAST=""
for i in $(seq 1 34); do
  LAST=$(curl -s -o /dev/null -w '%{http_code}' "$GW/api/v1/products?q=widget$i")
done
[ "$LAST" = "429" ] && pass "search throttled at the edge -> 429" || fail "edge limit" "last=$LAST"
H=$(curl -s -D- -o /dev/null "$GW/api/v1/products?q=x" | tr -d '\r')
grep -qi '^ratelimit-limit: 30' <<<"$H" && pass "RateLimit-Limit: 30 advertised" || fail "headers" "$(grep -i ratelimit <<<"$H" | head -2)"


exit $FAILED
