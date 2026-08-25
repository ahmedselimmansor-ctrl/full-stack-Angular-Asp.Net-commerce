#!/usr/bin/env bash
# Creates the Pub/Sub topics and subscriptions the saga needs.
#
# Against the local emulator by default; point PUBSUB_HOST at a real endpoint (and provide
# credentials) to provision an actual project. Every call is idempotent: an existing
# topic/subscription returns 409, which is treated as success.
set -uo pipefail

PROJECT="${PUBSUB_PROJECT:-sama-local}"
HOST="${PUBSUB_HOST:-http://127.0.0.1:8085}"
PREFIX="${PUBSUB_PREFIX:-sama}"

topic() {
  local name="$PREFIX-$1"
  local code
  code=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$HOST/v1/projects/$PROJECT/topics/$name")
  case "$code" in
    200) echo "  topic $name created" ;;
    409) echo "  topic $name already exists" ;;
    *)   echo "  topic $name FAILED ($code)"; return 1 ;;
  esac
}

# subscription <name> <topic> [filter]
subscription() {
  local name="$PREFIX-$1" tname="$PREFIX-$2" filter="${3:-}"
  local body code

  body=$(python3 - "$PROJECT" "$tname" "$filter" <<'PY'
import json, sys
project, topic, flt = sys.argv[1], sys.argv[2], sys.argv[3]
payload = {
    "topic": f"projects/{project}/topics/{topic}",
    # Long enough for a handler that writes to Postgres and waits on the outbox, short enough
    # that a wedged consumer releases the message rather than stalling the saga.
    "ackDeadlineSeconds": 60,
    "messageRetentionDuration": "604800s",
    "enableMessageOrdering": True,
}
if flt:
    payload["filter"] = flt
print(json.dumps(payload))
PY
)

  code=$(curl -s -o /tmp/sub_out.json -w '%{http_code}' -X PUT \
    -H 'Content-Type: application/json' -d "$body" \
    "$HOST/v1/projects/$PROJECT/subscriptions/$name")

  case "$code" in
    200) echo "  subscription $name -> $tname created" ;;
    409) echo "  subscription $name already exists" ;;
    400)
      # The emulator has not always supported server-side filters. Falling back is safe: the
      # subscriber acknowledges and discards event types it has no handler for.
      if [ -n "$filter" ]; then
        echo "  subscription $name: filter rejected, retrying unfiltered"
        subscription "$1" "$2" ""
      else
        echo "  subscription $name FAILED (400): $(head -c 200 /tmp/sub_out.json)"; return 1
      fi
      ;;
    *) echo "  subscription $name FAILED ($code)"; return 1 ;;
  esac
}

echo "Provisioning Pub/Sub on $HOST (project $PROJECT, prefix $PREFIX)"

echo "Topics — one per publishing service:"
for t in identity catalog basket ordering inventory payment; do topic "$t"; done

echo "Subscriptions — filtered to the event types each consumer actually handles:"

# Inventory reacts to the order lifecycle: hold stock, then release it if the order dies.
subscription inventory-ordering ordering \
  'attributes.eventType = "ordering.order.placed.v1" OR attributes.eventType = "ordering.order.cancelled.v1"'

# Payment charges only once inventory has confirmed the hold.
subscription payment-ordering ordering \
  'attributes.eventType = "ordering.payment.requested.v1"'

# Basket empties only after the sale is committed.
subscription basket-ordering ordering \
  'attributes.eventType = "ordering.order.confirmed.v1"'

# Ordering is the orchestrator: every participant reports back to it.
subscription ordering-inventory inventory ''
subscription ordering-payment   payment   ''

echo "Done."
