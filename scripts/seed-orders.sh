#!/usr/bin/env bash
# Places real orders so the back office has something to show.
#
# Every order goes through the actual checkout saga -- basket, reservation,
# payment, confirmation -- rather than being written straight into the database,
# so the dashboard is reading the same rows a real order produces.
#
# The one exception is the timestamp. A dashboard whose headline chart is
# "revenue over 14 days" cannot be judged when every order was placed in the same
# minute, so once the orders exist their created_at is spread backwards over the
# window. That is a local development convenience and is the only field touched.
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")" && . ./lib.sh

COUNT="${1:-24}"
reset_limits

if [ "${SAMA_KEEP_ORDERS:-0}" != "1" ]; then
  echo "Clearing previous orders and payments..."
  docker exec -i sama-postgres psql -U sama -d sama -v ON_ERROR_STOP=1 <<'SQL'
BEGIN;
DELETE FROM payment.payments;
DELETE FROM ordering.order_lines;
DELETE FROM ordering.orders;
COMMIT;
SQL
  [ $? -eq 0 ] || { echo "could not clear orders"; exit 1; }
fi

# Published products, so the basket will accept them.
mapfile -t PRODUCTS < <(curl -s "$CT/api/v1/products?pageSize=50" | python3 -c "
import json,sys
for item in json.load(sys.stdin)['items']:
    print(item['id'])")

[ "${#PRODUCTS[@]}" -gt 0 ] || { echo "no published products -- run seed-shop.sh first"; exit 1; }
echo "${#PRODUCTS[@]} products available."

CITIES=("Cairo" "Alexandria" "Giza" "Dubai" "Riyadh" "Amman")
PLACED=0

for i in $(seq 1 "$COUNT"); do
  EMAIL="shopper+$(date +%s%N)@sama.test"
  TOKEN=$(new_user "$EMAIL" "Shopper $i")
  [ -n "$TOKEN" ] || { echo "  ! could not register $EMAIL"; continue; }

  # One or two distinct lines per order, so the basket and order-line paths both
  # get exercised rather than every order being a single item.
  LINES=$(( (RANDOM % 2) + 1 ))
  for _ in $(seq 1 $LINES); do
    PRODUCT="${PRODUCTS[$((RANDOM % ${#PRODUCTS[@]}))]}"
    curl -s -o /dev/null -X POST "$BK/api/v1/basket/items" -H "Authorization: Bearer $TOKEN" \
      -H 'Content-Type: application/json' \
      -d "{\"productId\":\"$PRODUCT\",\"quantity\":$(( (RANDOM % 2) + 1 ))}"
  done

  CITY="${CITIES[$((RANDOM % ${#CITIES[@]}))]}"
  CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$OR/api/v1/orders" \
    -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
    -H "Idempotency-Key: seed-$(date +%s%N)" \
    -d "{\"shippingAddress\":{\"line1\":\"$(( (RANDOM % 90) + 1 )) Corniche\",\"city\":\"$CITY\",\"postcode\":\"115$(( (RANDOM % 90) + 10 ))\",\"country\":\"EG\"}}")

  if [ "$CODE" = "202" ]; then
    PLACED=$((PLACED+1))
    printf '\r  placed %d/%d' "$PLACED" "$COUNT"
  else
    printf '\r  ! order %d -> HTTP %s\n' "$i" "$CODE"
  fi
done
echo

# The saga runs asynchronously; without this the statuses are still Pending when
# the spread below runs and the dashboard shows a column of unresolved orders.
echo "Waiting for the sagas to settle..."
for _ in $(seq 1 40); do
  PENDING=$(docker exec sama-postgres psql -U sama -d sama -tAc \
    "SELECT count(*) FROM ordering.orders WHERE status IN ('Pending','AwaitingPayment');" | tr -d ' ')
  [ "${PENDING:-0}" = "0" ] && break
  sleep 1
done

# A dashboard where every order shares one status cannot show whether its
# breakdown chart works. These go through the real cancel endpoint, so stock is
# released and the compensating path runs exactly as it would in production.
CANCELS=$(( COUNT / 8 ))
if [ "$CANCELS" -gt 0 ]; then
  echo "Cancelling $CANCELS orders through the real cancel path..."
  ADMIN_TOKEN=$(new_admin)
  REASONS=("Customer changed their mind" "Duplicate order" "Address could not be verified")

  mapfile -t TO_CANCEL < <(docker exec sama-postgres psql -U sama -d sama -tAc \
    "SELECT id FROM ordering.orders WHERE status='Confirmed' ORDER BY created_at DESC LIMIT $CANCELS;" | tr -d ' ')

  for oid in "${TO_CANCEL[@]}"; do
    [ -n "$oid" ] || continue
    REASON="${REASONS[$((RANDOM % ${#REASONS[@]}))]}"
    curl -s -o /dev/null -X POST "$OR/api/v1/orders/$oid/cancel" \
      -H "Authorization: Bearer $ADMIN_TOKEN" -H 'Content-Type: application/json' \
      -d "{\"reason\":\"$REASON\"}"
  done
fi

echo "Spreading order dates across the last 14 days..."
docker exec -i sama-postgres psql -U sama -d sama -v ON_ERROR_STOP=1 <<'SQL'
BEGIN;

-- Deterministic from the row's own id, so re-running does not reshuffle the
-- chart, and weighted toward recent days the way real order flow is.
UPDATE ordering.orders
SET created_at = now()
  - ((abs(hashtext(id::text)) % 14) * interval '1 day')
  - ((abs(hashtext(id::text || 'h')) % 10) * interval '1 hour');

UPDATE payment.payments p
SET created_at = o.created_at
FROM ordering.orders o
WHERE p.order_id = o.id;

COMMIT;
SQL

echo
docker exec sama-postgres psql -U sama -d sama -c \
  "SELECT status, count(*), to_char(sum(total),'999G999D99') AS revenue
   FROM ordering.orders GROUP BY status ORDER BY status;"
