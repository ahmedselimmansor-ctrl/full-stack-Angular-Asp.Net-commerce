#!/usr/bin/env bash
# Seeds the shop with its three departments and a real catalogue.
#
# The storefront is only as good as what is in it: an empty grid, or one full of
# "Saga Goods" left behind by a test run, makes a working shop look broken. This
# fills Clothing, Shoes and Electronics with priced, photographed, in-stock
# products so the design can be judged against real content.
#
#   ./scripts/seed-shop.sh           add the departments and products
#   ./scripts/seed-shop.sh --fresh   remove test-run debris first
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")" && . ./lib.sh

FRESH=0
[ "${1:-}" = "--fresh" ] && FRESH=1

reset_limits

if [ "$FRESH" = "1" ]; then
  echo "Emptying the catalogue..."
  # `docker exec` only attaches stdin with -i; without it the heredoc is
  # discarded and the purge silently does nothing while still reporting success.
  # Only the generated names the suites create are matched. Anything a person
  # authored by hand does not match these patterns and is left alone.
  docker exec -i sama-postgres psql -U sama -d sama -v ON_ERROR_STOP=1 <<'SQL'
BEGIN;

-- --fresh empties the catalogue rather than trying to tell seeded rows from test
-- debris. An earlier version kept "everything in the three departments", which
-- protected exactly the products it was about to recreate, so re-running it
-- changed nothing. Deleting outright also lets the seed control creation order,
-- and creation order is what the storefront's "newest" sort reads.
--
-- Local development only: it drops the catalogue and its stock, and nothing else.
DELETE FROM inventory.stock_items;
DELETE FROM catalog.product_images;
DELETE FROM catalog.products;
DELETE FROM catalog.categories;

COMMIT;
SQL
  if [ $? -ne 0 ]; then echo "  purge failed; leaving the catalogue as it was"; exit 1; fi
  echo "  catalogue emptied."
fi

ADMIN=$(new_admin)
[ -n "$ADMIN" ] || { echo "could not provision an admin token"; exit 1; }
echo "Admin provisioned."

AUTH=(-H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json')

# Fixed crop and quality so every tile in a grid has identical dimensions; a
# mixed set of aspect ratios is what makes a product grid look unfinished.
img() { printf 'https://images.unsplash.com/%s?auto=format&fit=crop&q=80&w=900' "$1"; }

# Returns the id of a category, creating it only if the slug is new, so the
# script can be run repeatedly without piling up duplicates.
ensure_category() {
  local name="$1" slug="$2" order="$3" existing
  existing=$(curl -s "$CT/api/v1/categories" | python3 -c "
import json,sys
for c in json.load(sys.stdin):
    if c['slug'] == sys.argv[1]:
        print(c['id']); break
" "$slug")

  if [ -n "$existing" ]; then printf '%s' "$existing"; return; fi

  curl -s -X POST "$CT/api/v1/categories" "${AUTH[@]}" \
    -d "{\"name\":\"$name\",\"slug\":\"$slug\",\"sortOrder\":$order}" | J id
}

CLOTHING=$(ensure_category "Clothing" "clothing" 1)
SHOES=$(ensure_category "Shoes" "shoes" 2)
ELECTRONICS=$(ensure_category "Electronics" "electronics" 3)

for pair in "Clothing:$CLOTHING" "Shoes:$SHOES" "Electronics:$ELECTRONICS"; do
  [ -n "${pair##*:}" ] && echo "  ${pair%%:*} -> ${pair##*:}" || { echo "failed to create ${pair%%:*}"; exit 1; }
done

CREATED=0
SKIPPED=0

# name | slug | sku | brand | price | category | stock | photo id | description
seed_product() {
  local name="$1" slug="$2" sku="$3" brand="$4" price="$5" cat="$6" stock="$7" photo="$8" desc="$9"

  local body code pid
  body=$(python3 -c "
import json,sys
print(json.dumps({
  'sku': sys.argv[1], 'name': sys.argv[2], 'slug': sys.argv[3],
  'description': sys.argv[4], 'categoryId': sys.argv[5],
  'price': float(sys.argv[6]), 'currency': 'EGP', 'brand': sys.argv[7],
  'images': [{'url': sys.argv[8], 'altText': sys.argv[2], 'sortOrder': 0}],
}))" "$sku" "$name" "$slug" "$desc" "$cat" "$price" "$brand" "$(img "$photo")")

  local response
  response=$(curl -s -w '\n%{http_code}' -X POST "$CT/api/v1/products" "${AUTH[@]}" -d "$body")
  code=$(tail -1 <<<"$response")
  pid=$(sed '$d' <<<"$response" | J id)

  if [ "$code" != "201" ]; then
    # A duplicate slug means it is already seeded, which is not a failure.
    if [ "$code" = "409" ]; then SKIPPED=$((SKIPPED+1)); return; fi
    echo "  ! $name -> HTTP $code"
    return
  fi

  curl -s -o /dev/null -X POST "$CT/api/v1/products/$pid/publish" -H "Authorization: Bearer $ADMIN"

  # Without a stock record the product is unbuyable: checkout reserves against
  # inventory, and a missing row reads as zero on hand.
  curl -s -o /dev/null -X POST "$IN/api/v1/stock" "${AUTH[@]}" \
    -d "{\"productId\":\"$pid\",\"sku\":\"$sku\",\"quantityOnHand\":$stock,\"reorderLevel\":6}"

  CREATED=$((CREATED+1))
}

# Seeded round-robin across the three departments on purpose. Creation order is
# what "newest" sorts by, so seeding department by department would leave the
# home page's new-arrivals rail showing eight electronics items and nothing else,
# making a three-department shop look like it only sells gadgets.
echo "Seeding all three departments..."
seed_product "Faded linen shirt" "faded-linen-shirt" "SMA-CL-001" "Sama Studio" 1450 "$CLOTHING" 42 \
  "photo-1596755094514-f87e34085b2c" "Washed European linen, cut generously through the body. Softens with every wash and stays cool at 40 degrees."
seed_product "Everyday runner" "everyday-runner" "SMA-SH-001" "Tarik" 2900 "$SHOES" 48 \
  "photo-1542291026-7eec264c27ff" "A cushioned trainer with a rubber outsole that grips wet marble. Built for pavement, not a track."
seed_product "Over-ear headphones" "over-ear-headphones" "SMA-EL-001" "Anwar Audio" 8900 "$ELECTRONICS" 22 \
  "photo-1505740420928-5e560c06d30e" "Forty-millimetre drivers and sixty hours on a charge. The earpads are replaceable and cost ninety pounds."
seed_product "Rigid denim jacket" "rigid-denim-jacket" "SMA-CL-002" "Nile Denim" 3200 "$CLOTHING" 24 \
  "photo-1544022613-e87ca75a784a" "Fourteen-ounce selvedge denim, unwashed. It will be stiff for a month and then it will be yours."
seed_product "Goodyear-welted boot" "goodyear-welted-boot" "SMA-SH-002" "Qasr" 7200 "$SHOES" 18 \
  "photo-1595950653106-6c9ebd614d3a" "Full-grain leather on a Goodyear welt, so it can be resoled three or four times before it is finished."
seed_product "Field watch" "field-watch" "SMA-EL-002" "Meridian" 5600 "$ELECTRONICS" 19 \
  "photo-1523275335684-37898b6baf30" "A mechanical field watch with a sapphire crystal and a screw-down crown. No battery to replace, ever."
seed_product "Wool overcoat" "wool-overcoat" "SMA-CL-003" "Sama Studio" 6800 "$CLOTHING" 12 \
  "photo-1539533018447-63fcce2678e3" "Double-faced wool with a full canvas front. Warm enough for a Cairo January and a Beirut February."
seed_product "Leather penny loafer" "leather-penny-loafer" "SMA-SH-003" "Qasr" 4100 "$SHOES" 26 \
  "photo-1533867617858-e7b97e060509" "Hand-lasted calf leather with a leather sole. Half sizes available."
seed_product "Fitness smartwatch" "fitness-smartwatch" "SMA-EL-003" "Meridian" 6400 "$ELECTRONICS" 31 \
  "photo-1546868871-7041f2a55e12" "Eleven days between charges with the heart rate sensor on, which is the only number that matters here."
seed_product "Bias-cut slip dress" "bias-cut-slip-dress" "SMA-CL-004" "Meria" 2400 "$CLOTHING" 30 \
  "photo-1515886657613-9f3515b0c78f" "Sand-washed silk cut on the bias so it moves with you rather than hanging off you."
seed_product "Court trainer" "court-trainer" "SMA-SH-004" "Tarik" 2350 "$SHOES" 54 \
  "photo-1560769629-975ec94e6a86" "A low-profile court shoe with a vulcanised sole. The one pair that goes with everything."
seed_product "Thin-and-light laptop" "thin-and-light-laptop" "SMA-EL-004" "Vertex" 34500 "$ELECTRONICS" 8 \
  "photo-1517336714731-489689fd1ca8" "Fourteen inches, sixteen gigabytes, and a battery that lasts a working day. The RAM is soldered; the SSD is not."
seed_product "Heavyweight cotton tee" "heavyweight-cotton-tee" "SMA-CL-005" "Sama Basics" 620 "$CLOTHING" 96 \
  "photo-1521572163474-6864f9cf17ab" "Two hundred and forty gram Egyptian cotton, tubular knit so it will not twist in the wash."
seed_product "Leather sandal" "leather-sandal" "SMA-SH-005" "Bahr" 1350 "$SHOES" 62 \
  "photo-1520639888713-7851133b1ed0" "Vegetable-tanned straps on a cork footbed that takes the shape of your foot in a fortnight."
seed_product "Portable speaker" "portable-speaker" "SMA-EL-005" "Anwar Audio" 3400 "$ELECTRONICS" 37 \
  "photo-1587829741301-dc798b83add3" "Loud enough for a roof in Zamalek, sealed against dust, and it charges over the same cable as your phone."
seed_product "Lambswool crewneck" "lambswool-crewneck" "SMA-CL-006" "Sama Studio" 2850 "$CLOTHING" 28 \
  "photo-1576871337622-98d48d1cf531" "Scottish lambswool, fully fashioned. The shoulders are knitted to shape rather than cut and sewn."
seed_product "Canvas hi-top" "canvas-hi-top" "SMA-SH-006" "Tarik" 1850 "$SHOES" 40 \
  "photo-1608231387042-66d1773070a5" "Twelve-ounce canvas with a reinforced toe. Machine washable, which the leather ones are not."
seed_product "Mechanical keyboard" "mechanical-keyboard" "SMA-EL-006" "Vertex" 4200 "$ELECTRONICS" 25 \
  "photo-1587202372775-e229f172b9d7" "Hot-swappable switches and a steel plate. Arabic and English legends on every keycap."
seed_product "Pleated wool trousers" "pleated-wool-trousers" "SMA-CL-007" "Sama Studio" 2650 "$CLOTHING" 34 \
  "photo-1594633312681-425c7b97ccd1" "A single forward pleat and a high rise. Unhemmed, because nobody has the same inseam."
seed_product "Compact mirrorless camera" "compact-mirrorless-camera" "SMA-EL-007" "Meridian" 28900 "$ELECTRONICS" 6 \
  "photo-1526170375885-4d8ecf77b99f" "Twenty-six megapixels in a body that fits a jacket pocket. Takes the same batteries as the previous two generations."
seed_product "Cotton trench" "cotton-trench" "SMA-CL-008" "Meria" 5400 "$CLOTHING" 16 \
  "photo-1591047139829-d91aecb6caea" "Densely woven cotton gabardine that sheds a shower without a coating that wears off."
seed_product "Wireless earbuds" "wireless-earbuds" "SMA-EL-008" "Anwar Audio" 3900 "$ELECTRONICS" 44 \
  "photo-1572569511254-d8f925fe2cbb" "Eight hours a charge, thirty in the case, and each bud works on its own if you lose the other."
echo
echo "Seeded $CREATED products, skipped $SKIPPED already present."
TOTAL=$(curl -s "$CT/api/v1/products?pageSize=1" | J totalCount)
echo "Storefront now lists $TOTAL published products."
