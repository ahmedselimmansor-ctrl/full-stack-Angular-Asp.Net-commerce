#!/usr/bin/env bash
# Catalogue: authoring, publication, anonymous browsing, CDN cache headers, conditional GETs,
# PostgreSQL full-text search, tag-based invalidation, and the outbox.
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")" && . ./lib.sh

reset_limits
STAMP=$(date +%s%N)
ADMIN=$(new_admin)
[ -n "$ADMIN" ] && pass "admin provisioned" || fail "admin" "no token"

echo "1) anonymous writes are rejected"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$CT/api/v1/categories" \
  -H 'Content-Type: application/json' -d '{"name":"X","slug":"x","sortOrder":0}')
[ "$CODE" = "401" ] && pass "anonymous create -> 401" || fail "anon write" "$CODE"

echo "2) create a category"
R=$(curl -s -w '\n%{http_code}' -X POST "$CT/api/v1/categories" -H "Authorization: Bearer $ADMIN" \
  -H 'Content-Type: application/json' -d "{\"name\":\"Coffee $STAMP\",\"slug\":\"coffee-$STAMP\",\"sortOrder\":1}")
CODE=$(tail -1 <<<"$R"); CATID=$(sed '$d' <<<"$R" | J id)
[ "$CODE" = "201" ] && pass "category -> 201" || fail "category" "$CODE"

echo "3) slug validation"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$CT/api/v1/categories" -H "Authorization: Bearer $ADMIN" \
  -H 'Content-Type: application/json' -d '{"name":"Bad","slug":"Not A Slug","sortOrder":0}')
[ "$CODE" = "400" ] && pass "invalid slug -> 400" || fail "slug validation" "$CODE"

echo "4) create and publish a product"
SLUG="espresso-$STAMP"
R=$(curl -s -w '\n%{http_code}' -X POST "$CT/api/v1/products" -H "Authorization: Bearer $ADMIN" \
  -H 'Content-Type: application/json' \
  -d "{\"sku\":\"ESP-$STAMP\",\"name\":\"Sama Espresso Blend\",\"slug\":\"$SLUG\",\"description\":\"A dark roast with cocoa and citrus notes.\",\"categoryId\":\"$CATID\",\"price\":24.50,\"currency\":\"USD\",\"brand\":\"Sama Roasters\",\"images\":[{\"url\":\"/media/espresso.jpg\",\"altText\":\"Bag of espresso\",\"sortOrder\":0}]}")
CODE=$(tail -1 <<<"$R"); PID=$(sed '$d' <<<"$R" | J id)
[ "$CODE" = "201" ] && pass "product -> 201" || fail "product" "$CODE"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$CT/api/v1/products/$PID/publish" -H "Authorization: Bearer $ADMIN")
[ "$CODE" = "204" ] && pass "publish -> 204" || fail "publish" "$CODE"

echo "5) anonymous browse and CDN headers"
H=$(curl -s -D- -o /tmp/sama_cat.json "$CT/api/v1/products?q=cocoa" | tr -d '\r')
grep -qi "^cache-control:.*s-maxage=300" <<<"$H" && pass "s-maxage=300 for the CDN" || fail "cache-control" "$(grep -i cache-control <<<"$H")"
grep -qi "stale-while-revalidate" <<<"$H" && pass "stale-while-revalidate set" || fail "swr" "missing"
grep -qi "^cache-tag:" <<<"$H" && pass "cache-tag for targeted purge" || fail "cache-tag" "missing"
ETAG=$(grep -i '^etag:' <<<"$H" | sed 's/^[Ee][Tt][Aa][Gg]: //')
[ -n "$ETAG" ] && pass "etag present" || fail "etag" "missing"

echo "6) full-text search"
N=$(J totalCount < /tmp/sama_cat.json)
[ "${N:-0}" -ge 1 ] && pass "'cocoa' matches the description ($N)" || fail "fts" "0 results"
N=$(curl -s "$CT/api/v1/products?q=zzzznotathing" | J totalCount)
[ "$N" = "0" ] && pass "no match -> 0" || fail "fts miss" "$N"

echo "7) conditional GET"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -H "If-None-Match: $ETAG" "$CT/api/v1/products?q=cocoa")
[ "$CODE" = "304" ] && pass "If-None-Match -> 304" || fail "conditional get" "$CODE"

echo "8) a write invalidates the cached view"
curl -s -o /dev/null -X PATCH "$CT/api/v1/products/$PID/price" -H "Authorization: Bearer $ADMIN" \
  -H 'Content-Type: application/json' -d '{"price":19.99,"currency":"USD"}'
sleep 1
NEW=$(curl -s -D- -o /tmp/sama_cat2.json "$CT/api/v1/products?q=cocoa" | tr -d '\r' | grep -i '^etag:' | sed 's/^[Ee][Tt][Aa][Gg]: //')
[ "$NEW" != "$ETAG" ] && pass "etag changed after write" || fail "invalidation" "etag unchanged"
P=$(J items.0.price < /tmp/sama_cat2.json)
[ "$P" = "19.99" ] && pass "new price served ($P)" || fail "price" "$P"

echo "9) the price change reached the outbox"
N=$(docker exec sama-postgres psql -U sama -d sama -tAc \
  "SELECT count(*) FROM catalog.outbox_messages WHERE type='catalog.product.priceChanged.v1';" | tr -d ' ')
[ "${N:-0}" -ge 1 ] && pass "priceChanged event written ($N)" || fail "outbox" "$N"

echo "10) a customer cannot write the catalogue"
CUST=$(new_user "cust+$STAMP@sama.test" "Customer")
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$CT/api/v1/products" -H "Authorization: Bearer $CUST" \
  -H 'Content-Type: application/json' \
  -d "{\"sku\":\"X-$STAMP\",\"name\":\"x\",\"slug\":\"x-$STAMP\",\"description\":\"d\",\"categoryId\":\"$CATID\",\"price\":1,\"currency\":\"USD\"}")
[ "$CODE" = "403" ] && pass "customer write -> 403" || fail "permission" "$CODE"

exit $FAILED
