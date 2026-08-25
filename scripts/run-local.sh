#!/usr/bin/env bash
# Starts every Sama service against the local Docker dependencies.
#
# Ports avoid 5432/6379 (often already taken) and 8085 (the Pub/Sub emulator).
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOGS="${SAMA_LOG_DIR:-$ROOT/.logs}"
mkdir -p "$LOGS"

export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export ASPNETCORE_ENVIRONMENT=Development

# Routes the Google client libraries at the emulator instead of real Pub/Sub.
export PUBSUB_EMULATOR_HOST="${PUBSUB_EMULATOR_HOST:-localhost:8085}"

# The OTLP exporter would otherwise retry against a collector that is not running locally.
export OTEL_SDK_DISABLED=true

# The gateway is the only port a client should need; the rest are exposed locally so each
# service can be exercised directly during development.
services=(
  "Identity:8081"
  "Catalog:8082"
  "Basket:8083"
  "Ordering:8084"
  "Inventory:8086"
  "Payment:8087"
)

GATEWAY_PORT=8080

case "${1:-start}" in
  stop)
    for entry in "${services[@]}" "Gateway:$GATEWAY_PORT"; do
      port="${entry##*:}"
      for pid in $( (ss -ltnp 2>/dev/null || true) | grep ":$port" | grep -oE 'pid=[0-9]+' | cut -d= -f2 | sort -u); do
        kill "$pid" 2>/dev/null && echo "stopped ${entry%%:*} (pid $pid)"
      done
    done
    exit 0
    ;;
  status)
    for entry in "Gateway:$GATEWAY_PORT" "${services[@]}"; do
      name="${entry%%:*}"; port="${entry##*:}"
      printf '  %-10s :%s  %s\n' "$name" "$port" \
        "$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$port/health/ready" 2>/dev/null)"
    done
    exit 0
    ;;
esac

for entry in "${services[@]}"; do
  name="${entry%%:*}"; port="${entry##*:}"
  ( cd "$ROOT/src/services/Sama.$name" && \
    ASPNETCORE_URLS="http://127.0.0.1:$port" \
    nohup dotnet run --no-launch-profile > "$LOGS/$name.log" 2>&1 & )
  echo "starting Sama.$name on :$port"
done

( cd "$ROOT/src/gateway/Sama.Gateway" && \
  ASPNETCORE_URLS="http://127.0.0.1:$GATEWAY_PORT" \
  nohup dotnet run --no-launch-profile > "$LOGS/Gateway.log" 2>&1 & )
echo "starting Sama.Gateway on :$GATEWAY_PORT"

echo "Waiting for readiness..."
for _ in $(seq 1 40); do
  ready=0
  for entry in "Gateway:$GATEWAY_PORT" "${services[@]}"; do
    port="${entry##*:}"
    [ "$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$port/health/ready" 2>/dev/null)" = "200" ] && ready=$((ready+1))
  done
  expected=$(( ${#services[@]} + 1 ))
  [ "$ready" -eq "$expected" ] && { echo "All $expected services ready."; exit 0; }
  sleep 3
done

echo "Timed out; current status:"
"$0" status
exit 1
