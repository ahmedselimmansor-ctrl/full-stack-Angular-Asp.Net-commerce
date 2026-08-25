#!/usr/bin/env bash
# End-to-end checkout saga: browse -> basket -> checkout -> stock -> payment -> confirmation,
# plus the compensating paths (decline, out-of-stock) and idempotent retry.
set -uo pipefail

ID=http://127.0.0.1:8081; CT=http://127.0.0.1:8082; BK=http://127.0.0.1:8083
OR=http://127.0.0.1:8084; IN=http://127.0.0.1:8086; PY_=http://127.0.0.1:8087
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

# Waits for an order to leave Pending, since the saga completes asynchronously.
await_status(){
  local id="$1" want="$2" tok="$3" s=""
  for _ in $(seq 1 40); do
    s=$(curl -s "$OR/api/v1/orders/$id" -H "Authorization: Bearer $tok" | J status)
    [ "$s" = "$want" ] && { echo "$s"; return 0; }
    [ "$s" = "Cancelled" ] && [ "$want" != "Cancelled" ] && { echo "$s"; return 1; }
    sleep 0.75
  done
  echo "$s"; return 1
}

docker exec sama-redis redis-cli FLUSHALL >/dev/null 2>&1

echo "0) provision admin + customer"
ADMIN_EMAIL="saga-admin+$(date +%s%N)@sama.test"
BODY=$(curl -s -X POST "$ID/api/v1/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$PW\",\"displayName\":\"Saga Admin\"}")
UID_=$(python3 -c "
import json,base64,sys
t=json.loads(sys.argv[1])['accessToken'].split('.')[1]; t+='='*(-len(t)%4)
print(json.loads(base64.urlsafe_b64decode(t))['sub'])" "$BODY")
docker exec sama-postgres psql -U sama -d sama -q -c \
  "INSERT INTO identity.user_roles(user_id,role) VALUES ('$UID_','administrator') ON CONFLICT DO NOTHING;" >/dev/null 2>&1
ADMIN=$(curl -s -X POST "$ID/api/v1/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$PW\"}" | J accessToken)
[ -n "$ADMIN" ] && pass "admin ready" || fail "admin" "no token"

CUST_EMAIL="saga-cust+$(date +%s%N)@sama.test"
CUST=$(curl -s -X POST "$ID/api/v1/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$CUST_EMAIL\",\"password\":\"$PW\",\"displayName\":\"Saga Customer\"}" | J accessToken)
[ -n "$CUST" ] && pass "customer ready" || fail "customer" "no token"

echo "1) catalogue + stock"
CATID=$(curl -s -X POST "$CT/api/v1/categories" -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d "{\"name\":\"Saga Goods\",\"slug\":\"saga-goods-$(date +%s)\",\"sortOrder\":1}" | J id)
SKU="SAGA-$(date +%s)"
PROD=$(curl -s -X POST "$CT/api/v1/products" -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d "{\"sku\":\"$SKU\",\"name\":\"Saga Widget\",\"slug\":\"saga-widget-$(date +%s)\",\"description\":\"A widget for exercising the saga.\",\"categoryId\":\"$CATID\",\"price\":25.00,\"currency\":\"USD\",\"images\":[{\"url\":\"/media/w.jpg\",\"altText\":\"w\",\"sortOrder\":0}]}" | J id)
curl -s -o /dev/null -X POST "$CT/api/v1/products/$PROD/publish" -H "Authorization: Bearer $ADMIN"
curl -s -o /dev/null -X POST "$IN/api/v1/stock" -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d "{\"productId\":\"$PROD\",\"sku\":\"$SKU\",\"quantityOnHand\":10,\"reorderLevel\":2}"
AVAIL=$(curl -s "$IN/api/v1/stock/$PROD" -H "Authorization: Bearer $ADMIN" | J available)
[ "$AVAIL" = "10" ] && pass "product published, 10 in stock" || fail "stock setup" "available=$AVAIL"

echo "2) happy path: basket -> checkout -> confirmed"
curl -s -o /dev/null -X POST "$BK/api/v1/basket/items" -H "Authorization: Bearer $CUST" -H 'Content-Type: application/json' \
  -d "{\"productId\":\"$PROD\",\"quantity\":2}"
KEY="idem-$(date +%s%N)"
R=$(curl -s -w '\n%{http_code}' -X POST "$OR/api/v1/orders" -H "Authorization: Bearer $CUST" \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $KEY" \
  -d '{"shippingAddress":{"line1":"1 Nile St","city":"Cairo","postcode":"11511","country":"EG"}}')
CODE=$(tail -1 <<<"$R"); OBODY=$(sed '$d' <<<"$R")
[ "$CODE" = "202" ] && pass "checkout -> 202 Accepted" || fail "checkout" "$CODE $OBODY"
ORDER=$(J id <<<"$OBODY"); ONUM=$(J orderNumber <<<"$OBODY")
echo "     order $ONUM ($ORDER)"

ST=$(await_status "$ORDER" Confirmed "$CUST")
[ "$ST" = "Confirmed" ] && pass "saga completed: order Confirmed" || fail "saga" "ended as $ST"

echo "3) saga side effects"
AVAIL=$(curl -s "$IN/api/v1/stock/$PROD" -H "Authorization: Bearer $ADMIN" | J available)
RES=$(curl -s "$IN/api/v1/stock/$PROD" -H "Authorization: Bearer $ADMIN" | J reserved)
{ [ "$AVAIL" = "8" ] && [ "$RES" = "2" ]; } && pass "stock reserved (available=$AVAIL reserved=$RES)" || fail "stock" "available=$AVAIL reserved=$RES"

PSTAT=$(curl -s "$PY_/api/v1/payments" -H "Authorization: Bearer $CUST" | J items.0.status)
[ "$PSTAT" = "Succeeded" ] && pass "payment Succeeded" || fail "payment" "$PSTAT"

for _ in $(seq 1 15); do
  BQ=$(curl -s "$BK/api/v1/basket" -H "Authorization: Bearer $CUST" | J totalQuantity)
  [ "$BQ" = "0" ] && break; sleep 0.75
done
[ "$BQ" = "0" ] && pass "basket cleared on confirmation" || fail "basket clear" "qty=$BQ"

echo "4) idempotent retry returns the same order"
R2=$(curl -s -X POST "$OR/api/v1/orders" -H "Authorization: Bearer $CUST" \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $KEY" \
  -d '{"shippingAddress":{"line1":"1 Nile St","city":"Cairo","postcode":"11511","country":"EG"}}')
[ "$(J id <<<"$R2")" = "$ORDER" ] && pass "same key -> same order, no duplicate" || fail "idempotency" "$(J id <<<"$R2")"
N=$(docker exec sama-postgres psql -U sama -d sama -tAc "SELECT count(*) FROM ordering.orders WHERE order_number='$ONUM';" | tr -d ' ')
[ "$N" = "1" ] && pass "exactly one order row" || fail "duplicate orders" "$N"

echo "5) missing Idempotency-Key is rejected"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$OR/api/v1/orders" -H "Authorization: Bearer $CUST" \
  -H 'Content-Type: application/json' -d '{"shippingAddress":{"line1":"a","city":"b","postcode":"c","country":"EG"}}')
[ "$CODE" = "400" ] && pass "no key -> 400" || fail "key required" "$CODE"

echo "6) compensating path: payment declined (.13 amount) releases stock"
SKU2="SAGA13-$(date +%s)"
PROD2=$(curl -s -X POST "$CT/api/v1/products" -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d "{\"sku\":\"$SKU2\",\"name\":\"Decline Widget\",\"slug\":\"decline-widget-$(date +%s)\",\"description\":\"Priced to trigger a simulated decline.\",\"categoryId\":\"$CATID\",\"price\":13.13,\"currency\":\"USD\",\"images\":[{\"url\":\"/media/d.jpg\",\"altText\":\"d\",\"sortOrder\":0}]}" | J id)
curl -s -o /dev/null -X POST "$CT/api/v1/products/$PROD2/publish" -H "Authorization: Bearer $ADMIN"
curl -s -o /dev/null -X POST "$IN/api/v1/stock" -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d "{\"productId\":\"$PROD2\",\"sku\":\"$SKU2\",\"quantityOnHand\":5,\"reorderLevel\":0}"
curl -s -o /dev/null -X POST "$BK/api/v1/basket/items" -H "Authorization: Bearer $CUST" -H 'Content-Type: application/json' \
  -d "{\"productId\":\"$PROD2\",\"quantity\":1}"
O2=$(curl -s -X POST "$OR/api/v1/orders" -H "Authorization: Bearer $CUST" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: idem-$(date +%s%N)" \
  -d '{"shippingAddress":{"line1":"1 Nile St","city":"Cairo","postcode":"11511","country":"EG"}}' | J id)
ST2=$(await_status "$O2" Cancelled "$CUST")
[ "$ST2" = "Cancelled" ] && pass "declined payment -> order Cancelled" || fail "decline path" "$ST2"
sleep 2
A2=$(curl -s "$IN/api/v1/stock/$PROD2" -H "Authorization: Bearer $ADMIN" | J available)
R2S=$(curl -s "$IN/api/v1/stock/$PROD2" -H "Authorization: Bearer $ADMIN" | J reserved)
{ [ "$A2" = "5" ] && [ "$R2S" = "0" ]; } && pass "stock released by compensation (available=$A2 reserved=$R2S)" || fail "compensation" "available=$A2 reserved=$R2S"

echo "7) compensating path: insufficient stock"
SKU3="SAGAOOS-$(date +%s)"
PROD3=$(curl -s -X POST "$CT/api/v1/products" -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d "{\"sku\":\"$SKU3\",\"name\":\"Scarce Widget\",\"slug\":\"scarce-widget-$(date +%s)\",\"description\":\"Only one in stock.\",\"categoryId\":\"$CATID\",\"price\":10.00,\"currency\":\"USD\",\"images\":[{\"url\":\"/media/s.jpg\",\"altText\":\"s\",\"sortOrder\":0}]}" | J id)
curl -s -o /dev/null -X POST "$CT/api/v1/products/$PROD3/publish" -H "Authorization: Bearer $ADMIN"
curl -s -o /dev/null -X POST "$IN/api/v1/stock" -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d "{\"productId\":\"$PROD3\",\"sku\":\"$SKU3\",\"quantityOnHand\":1,\"reorderLevel\":0}"
curl -s -o /dev/null -X POST "$BK/api/v1/basket/items" -H "Authorization: Bearer $CUST" -H 'Content-Type: application/json' \
  -d "{\"productId\":\"$PROD3\",\"quantity\":5}"
O3=$(curl -s -X POST "$OR/api/v1/orders" -H "Authorization: Bearer $CUST" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: idem-$(date +%s%N)" \
  -d '{"shippingAddress":{"line1":"1 Nile St","city":"Cairo","postcode":"11511","country":"EG"}}' | J id)
ST3=$(await_status "$O3" Cancelled "$CUST")
[ "$ST3" = "Cancelled" ] && pass "out of stock -> order Cancelled" || fail "oos path" "$ST3"
N=$(docker exec sama-postgres psql -U sama -d sama -tAc "SELECT count(*) FROM payment.payments WHERE order_id='$O3';" | tr -d ' ')
[ "$N" = "0" ] && pass "no payment attempted for rejected order" || fail "premature charge" "$N payments"

echo "8) data access control"
OTHER=$(curl -s -X POST "$ID/api/v1/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"nosy+$(date +%s%N)@sama.test\",\"password\":\"$PW\",\"displayName\":\"Nosy\"}" | J accessToken)
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$OR/api/v1/orders/$ORDER" -H "Authorization: Bearer $OTHER")
[ "$CODE" = "404" ] && pass "another customer's order -> 404 (not 403)" || fail "row-level access" "$CODE"
N=$(curl -s "$OR/api/v1/orders" -H "Authorization: Bearer $OTHER" | J totalCount)
[ "$N" = "0" ] && pass "listing scoped to own orders" || fail "scope" "$N"
N=$(curl -s "$OR/api/v1/orders" -H "Authorization: Bearer $ADMIN" | J totalCount)
[ "$N" -ge 3 ] && pass "admin sees all orders ($N)" || fail "admin scope" "$N"

echo "9) outbox fully drained"
PEND=$(docker exec sama-postgres psql -U sama -d sama -tAc \
  "SELECT coalesce(sum(c),0) FROM (SELECT count(*) c FROM ordering.outbox_messages WHERE processed_at IS NULL UNION ALL SELECT count(*) FROM inventory.outbox_messages WHERE processed_at IS NULL UNION ALL SELECT count(*) FROM payment.outbox_messages WHERE processed_at IS NULL) t;" | tr -d ' ')
[ "$PEND" = "0" ] && pass "no unpublished outbox rows" || fail "outbox drain" "$PEND pending"

exit $FAILED
