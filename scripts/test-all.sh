#!/usr/bin/env bash
# Runs every end-to-end suite against a locally running stack.
#
#   docker compose up -d
#   ./scripts/run-local.sh start
#   ./scripts/test-all.sh
#
# Order matters: the catalogue suite creates the product the basket and saga suites shop for.
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

SUITES=(test-auth.sh test-catalog.sh test-basket.sh test-saga.sh test-gateway.sh)
FAILED=()

for suite in "${SUITES[@]}"; do
  printf '\n\033[1m══ %s ══\033[0m\n' "$suite"
  if ./"$suite"; then
    printf '\033[32m%s passed\033[0m\n' "$suite"
  else
    printf '\033[31m%s FAILED\033[0m\n' "$suite"
    FAILED+=("$suite")
  fi
done

printf '\n\033[1m══ summary ══\033[0m\n'
if [ ${#FAILED[@]} -eq 0 ]; then
  printf '\033[32mAll %d suites passed.\033[0m\n' "${#SUITES[@]}"
  exit 0
fi

printf '\033[31m%d of %d suites failed: %s\033[0m\n' "${#FAILED[@]}" "${#SUITES[@]}" "${FAILED[*]}"
exit 1
