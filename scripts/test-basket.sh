#!/usr/bin/env bash
# Basket: server-authoritative pricing, quantity caps, per-user isolation, no-store caching.
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")" && . ./lib.sh

reset_limits
STAMP=$(date +%s%N)
AT=$(new_user "shopper+$STAMP@sama.test" "Shopper")
[ -n "$AT" ] && pass "shopper registered" || fail "register" "no token"

PID=$(curl -s "$CT/api/v1/products?pageSize=1" | J items.0.id)
[ -n "$PID" ] && pass "catalogue has a product" || fail "product" "none — run test-catalog.sh first"
PRICE=$(curl -s "$CT/api/v1/products?pageSize=1" | J items.0.price)

echo "1) a new basket is empty"
N=$(curl -s "$BK/api/v1/basket" -H "Authorization: Bearer $AT" | J totalQuantity)
[ "$N" = "0" ] && pass "starts empty" || fail "empty" "$N"

echo "2) baskets are never cached"
H=$(curl -s -D- -o /dev/null "$BK/api/v1/basket" -H "Authorization: Bearer $AT" | tr -d '\r')
grep -qi "^cache-control:.*no-store" <<<"$H" && pass "Cache-Control: no-store" || fail "no-store" "$(grep -i cache-control <<<"$H")"

echo "3) price comes from the catalogue"
R=$(curl -s -X POST "$BK/api/v1/basket/items" -H "Authorization: Bearer $AT" \
  -H 'Content-Type: application/json' -d "{\"productId\":\"$PID\",\"quantity\":2}")
Q=$(J totalQuantity <<<"$R"); P=$(J items.0.unitPrice <<<"$R")
[ "$Q" = "2" ] && pass "quantity 2" || fail "add" "$Q"
[ "$P" = "$PRICE" ] && pass "server-side price ($P)" || fail "price" "got $P, catalogue says $PRICE"

echo "4) a client-supplied price is ignored"
R=$(curl -s -X POST "$BK/api/v1/basket/items" -H "Authorization: Bearer $AT" \
  -H 'Content-Type: application/json' -d "{\"productId\":\"$PID\",\"quantity\":1,\"unitPrice\":0.01}")
P=$(J items.0.unitPrice <<<"$R")
[ "$P" = "$PRICE" ] && pass "injected price ignored ($P)" || fail "price injection" "$P"

echo "5) per-line quantity is capped"
curl -s -o /dev/null -X PUT "$BK/api/v1/basket/items/$PID" -H "Authorization: Bearer $AT" \
  -H 'Content-Type: application/json' -d '{"quantity":999}'
Q=$(curl -s "$BK/api/v1/basket" -H "Authorization: Bearer $AT" | J items.0.quantity)
[ "$Q" = "20" ] && pass "clamped to 20" || fail "cap" "$Q"

echo "6) unknown product"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BK/api/v1/basket/items" -H "Authorization: Bearer $AT" \
  -H 'Content-Type: application/json' -d '{"productId":"01a03721-0000-7000-8000-00000000dead","quantity":1}')
[ "$CODE" = "404" ] && pass "unknown product -> 404" || fail "unknown" "$CODE"

echo "7) zero quantity removes the line"
curl -s -o /dev/null -X PUT "$BK/api/v1/basket/items/$PID" -H "Authorization: Bearer $AT" \
  -H 'Content-Type: application/json' -d '{"quantity":0}'
N=$(curl -s "$BK/api/v1/basket" -H "Authorization: Bearer $AT" | J totalQuantity)
[ "$N" = "0" ] && pass "line removed" || fail "remove" "$N"

echo "8) baskets are private to their owner"
AT2=$(new_user "other+$STAMP@sama.test" "Other")
curl -s -o /dev/null -X POST "$BK/api/v1/basket/items" -H "Authorization: Bearer $AT2" \
  -H 'Content-Type: application/json' -d "{\"productId\":\"$PID\",\"quantity\":3}"
A=$(curl -s "$BK/api/v1/basket" -H "Authorization: Bearer $AT"  | J totalQuantity)
B=$(curl -s "$BK/api/v1/basket" -H "Authorization: Bearer $AT2" | J totalQuantity)
{ [ "$A" = "0" ] && [ "$B" = "3" ]; } && pass "isolated (user1=$A user2=$B)" || fail "isolation" "user1=$A user2=$B"

echo "9) anonymous access is refused"
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$BK/api/v1/basket")
[ "$CODE" = "401" ] && pass "anonymous -> 401" || fail "anon" "$CODE"

exit $FAILED
