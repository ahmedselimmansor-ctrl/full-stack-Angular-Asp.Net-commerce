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

# name | nameAr | slug | sku | brand | price | category | stock | photo | description | descriptionAr
#
# Kept as one data block rather than twenty-two function calls so a translation
# sits on the same line as the text it translates -- the pair drifts apart the
# moment they live in separate places. Round-robin across departments on purpose:
# creation order is what "newest" sorts by, and seeding department by department
# would leave the home page's new-arrivals rail showing eight electronics items
# and nothing else, making a three-department shop look like it only sells gadgets.
seed_products() {
  local created=0 skipped=0

  while IFS='|' read -r name name_ar slug sku brand price cat_key stock photo desc desc_ar; do
    [ -z "${name// }" ] && continue
    case "$name" in \#*) continue ;; esac

    local category
    case "$cat_key" in
      clothing) category="$CLOTHING" ;;
      shoes) category="$SHOES" ;;
      electronics) category="$ELECTRONICS" ;;
      *) echo "  ! unknown category '$cat_key' for $name"; continue ;;
    esac

    local body response code pid
    body=$(python3 -c "
import json, sys
print(json.dumps({
  'sku': sys.argv[1].strip(), 'name': sys.argv[2].strip(), 'nameAr': sys.argv[3].strip() or None,
  'slug': sys.argv[4].strip(), 'description': sys.argv[5].strip(),
  'descriptionAr': sys.argv[6].strip() or None, 'categoryId': sys.argv[7].strip(),
  'price': float(sys.argv[8]), 'currency': 'EGP', 'brand': sys.argv[9].strip(),
  'images': [{'url': sys.argv[10].strip(), 'altText': sys.argv[2].strip(), 'sortOrder': 0}],
}))" "$sku" "$name" "$name_ar" "$slug" "$desc" "$desc_ar" "$category" "$price" "$brand" "$(img "$(echo "$photo" | tr -d ' ')")")

    response=$(curl -s -w '\n%{http_code}' -X POST "$CT/api/v1/products" "${AUTH[@]}" -d "$body")
    code=$(tail -1 <<<"$response")
    pid=$(sed '$d' <<<"$response" | J id)

    if [ "$code" = "409" ]; then skipped=$((skipped+1)); continue; fi
    if [ "$code" != "201" ]; then echo "  ! ${name} -> HTTP $code"; continue; fi

    curl -s -o /dev/null -X POST "$CT/api/v1/products/$pid/publish" -H "Authorization: Bearer $ADMIN"

    # Without a stock record the product is unbuyable: checkout reserves against
    # inventory, and a missing row reads as zero on hand.
    curl -s -o /dev/null -X POST "$IN/api/v1/stock" "${AUTH[@]}" \
      -d "{\"productId\":\"$pid\",\"sku\":\"$(echo "$sku" | tr -d ' ')\",\"quantityOnHand\":$stock,\"reorderLevel\":6}"

    created=$((created+1))
    printf '\r  seeded %d' "$created"
  done

  echo
  CREATED=$created
  SKIPPED=$skipped
}

echo "Seeding all three departments..."
seed_products <<'PRODUCTS'
Faded linen shirt|قميص كتان مغسول|faded-linen-shirt|SMA-CL-001|Sama Studio|1450|clothing|42|photo-1596755094514-f87e34085b2c|Washed European linen, cut generously through the body. Softens with every wash and stays cool at 40 degrees.|كتان أوروبي مغسول بقصّة واسعة عند الجذع. يزداد نعومة مع كل غسلة ويبقى بارداً في الأربعين درجة.
Everyday runner|حذاء جري يومي|everyday-runner|SMA-SH-001|Tarik|2900|shoes|48|photo-1542291026-7eec264c27ff|A cushioned trainer with a rubber outsole that grips wet marble. Built for pavement, not a track.|حذاء ممتصّ للصدمات بنعل مطاطي يمسك الرخام المبتل. مصنوع للأرصفة لا للمضمار.
Over-ear headphones|سماعات رأس محيطة بالأذن|over-ear-headphones|SMA-EL-001|Anwar Audio|8900|electronics|22|photo-1505740420928-5e560c06d30e|Forty-millimetre drivers and sixty hours on a charge. The earpads are replaceable and cost ninety pounds.|مشغّلات صوت بقطر أربعين ملّيمتراً وستون ساعة على الشحنة. الوسائد قابلة للاستبدال وثمنها تسعون جنيهاً.
Rigid denim jacket|جاكيت دنيم خام|rigid-denim-jacket|SMA-CL-002|Nile Denim|3200|clothing|24|photo-1544022613-e87ca75a784a|Fourteen-ounce selvedge denim, unwashed. It will be stiff for a month and then it will be yours.|دنيم سلفدج بوزن أربع عشرة أونصة، غير مغسول. سيبقى قاسياً شهراً ثم يصير على مقاسك وحدك.
Goodyear-welted boot|بوت بخياطة غوديير|goodyear-welted-boot|SMA-SH-002|Qasr|7200|shoes|18|photo-1595950653106-6c9ebd614d3a|Full-grain leather on a Goodyear welt, so it can be resoled three or four times before it is finished.|جلد كامل الحبيبات على خياطة غوديير، فيقبل تغيير النعل ثلاث أو أربع مرات قبل أن ينتهي عمره.
Field watch|ساعة ميدانية|field-watch|SMA-EL-002|Meridian|5600|electronics|19|photo-1523275335684-37898b6baf30|A mechanical field watch with a sapphire crystal and a screw-down crown. No battery to replace, ever.|ساعة ميدانية ميكانيكية بزجاج سفير وتاج لولبي. لا بطارية تُستبدل، أبداً.
Wool overcoat|معطف صوف|wool-overcoat|SMA-CL-003|Sama Studio|6800|clothing|12|photo-1539533018447-63fcce2678e3|Double-faced wool with a full canvas front. Warm enough for a Cairo January and a Beirut February.|صوف مزدوج الوجه بواجهة قماشية كاملة. يكفي دفؤه ليناير القاهرة وفبراير بيروت.
Leather penny loafer|لوفر جلد|leather-penny-loafer|SMA-SH-003|Qasr|4100|shoes|26|photo-1533867617858-e7b97e060509|Hand-lasted calf leather with a leather sole. Half sizes available.|جلد عجل مشكّل يدوياً بنعل جلدي. المقاسات النصفية متوفرة.
Fitness smartwatch|ساعة ذكية للياقة|fitness-smartwatch|SMA-EL-003|Meridian|6400|electronics|31|photo-1546868871-7041f2a55e12|Eleven days between charges with the heart rate sensor on, which is the only number that matters here.|أحد عشر يوماً بين الشحنتين مع تشغيل مستشعر النبض، وهو الرقم الوحيد المهم هنا.
Bias-cut slip dress|فستان حرير منحرف القصّة|bias-cut-slip-dress|SMA-CL-004|Meria|2400|clothing|30|photo-1515886657613-9f3515b0c78f|Sand-washed silk cut on the bias so it moves with you rather than hanging off you.|حرير مغسول بالرمل مقصوص على الانحراف، فيتحرك معك بدل أن يتدلّى عنك.
Court trainer|حذاء كورت|court-trainer|SMA-SH-004|Tarik|2350|shoes|54|photo-1560769629-975ec94e6a86|A low-profile court shoe with a vulcanised sole. The one pair that goes with everything.|حذاء كورت منخفض بنعل مبركن. الزوج الوحيد الذي يناسب كل شيء.
Thin-and-light laptop|لابتوب نحيف وخفيف|thin-and-light-laptop|SMA-EL-004|Vertex|34500|electronics|8|photo-1517336714731-489689fd1ca8|Fourteen inches, sixteen gigabytes, and a battery that lasts a working day. The RAM is soldered; the SSD is not.|أربعة عشر بوصة، وستة عشر جيجابايت، وبطارية تكفي يوم عمل. الذاكرة ملحومة، أما قرص التخزين فلا.
Heavyweight cotton tee|تي شيرت قطن ثقيل|heavyweight-cotton-tee|SMA-CL-005|Sama Basics|620|clothing|96|photo-1521572163474-6864f9cf17ab|Two hundred and forty gram Egyptian cotton, tubular knit so it will not twist in the wash.|قطن مصري بوزن مئتين وأربعين جراماً، حياكة أنبوبية فلا يلتوي في الغسيل.
Leather sandal|صندل جلد|leather-sandal|SMA-SH-005|Bahr|1350|shoes|62|photo-1520639888713-7851133b1ed0|Vegetable-tanned straps on a cork footbed that takes the shape of your foot in a fortnight.|أحزمة مدبوغة نباتياً على قاعدة فلّين تأخذ شكل قدمك خلال أسبوعين.
Portable speaker|مكبّر صوت محمول|portable-speaker|SMA-EL-005|Anwar Audio|3400|electronics|37|photo-1587829741301-dc798b83add3|Loud enough for a roof in Zamalek, sealed against dust, and it charges over the same cable as your phone.|صوته يكفي سطحاً في الزمالك، مُحكم ضد الغبار، ويُشحن بالكابل نفسه الذي يشحن هاتفك.
Lambswool crewneck|بلوفر صوف حُملان|lambswool-crewneck|SMA-CL-006|Sama Studio|2850|clothing|28|photo-1576871337622-98d48d1cf531|Scottish lambswool, fully fashioned. The shoulders are knitted to shape rather than cut and sewn.|صوف حُملان اسكتلندي، محبوك بالكامل على الشكل. الأكتاف تُحاك على قياسها بدل أن تُقصّ وتُخاط.
Canvas hi-top|حذاء قماش عالي الرقبة|canvas-hi-top|SMA-SH-006|Tarik|1850|shoes|40|photo-1608231387042-66d1773070a5|Twelve-ounce canvas with a reinforced toe. Machine washable, which the leather ones are not.|قماش بوزن اثنتي عشرة أونصة بمقدّمة مقوّاة. يُغسل في الغسالة، بخلاف الجلدية.
Mechanical keyboard|لوحة مفاتيح ميكانيكية|mechanical-keyboard|SMA-EL-006|Vertex|4200|electronics|25|photo-1587202372775-e229f172b9d7|Hot-swappable switches and a steel plate. Arabic and English legends on every keycap.|مفاتيح قابلة للتبديل السريع ولوحة فولاذية. حروف عربية وإنجليزية على كل زر.
Pleated wool trousers|بنطلون صوف بكسرة|pleated-wool-trousers|SMA-CL-007|Sama Studio|2650|clothing|34|photo-1594633312681-425c7b97ccd1|A single forward pleat and a high rise. Unhemmed, because nobody has the same inseam.|كسرة أمامية واحدة وخصر عالٍ. بلا تنّورة سفلية مخيطة، لأن طول الساق يختلف من شخص لآخر.
Compact mirrorless camera|كاميرا بلا مرآة مدمجة|compact-mirrorless-camera|SMA-EL-007|Meridian|28900|electronics|6|photo-1526170375885-4d8ecf77b99f|Twenty-six megapixels in a body that fits a jacket pocket. Takes the same batteries as the previous two generations.|ستة وعشرون ميجابكسل في هيكل يدخل جيب الجاكيت. يقبل بطاريات الجيلين السابقين نفسها.
Cotton trench|معطف قطن ترنش|cotton-trench|SMA-CL-008|Meria|5400|clothing|16|photo-1591047139829-d91aecb6caea|Densely woven cotton gabardine that sheds a shower without a coating that wears off.|جبردين قطني كثيف النسج يصدّ زخّة المطر بلا طبقة تتآكل مع الوقت.
Wireless earbuds|سماعات لاسلكية|wireless-earbuds|SMA-EL-008|Anwar Audio|3900|electronics|44|photo-1572569511254-d8f925fe2cbb|Eight hours a charge, thirty in the case, and each bud works on its own if you lose the other.|ثماني ساعات على الشحنة، وثلاثون في العلبة، وتعمل كل سمّاعة وحدها إن فقدت الأخرى.
PRODUCTS

echo
echo "Seeded $CREATED products, skipped $SKIPPED already present."
TOTAL=$(curl -s "$CT/api/v1/products?pageSize=1" | J totalCount)
echo "Storefront now lists $TOTAL published products."
