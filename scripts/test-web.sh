#!/usr/bin/env bash
# Runs the Angular unit tests.
#
# Vitest passes absolute paths through as glob patterns, and this checkout's
# directory name contains parentheses, which are extglob syntax -- so the path
# never matches itself and the runner reports "No test files found" after having
# just listed them. Nothing in the repository can fix that; the only cure is a
# path without parentheses.
#
# Rather than require everyone to rename their checkout, this stages the sources
# at a clean path and runs there. node_modules is symlinked, not copied.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WEB="$ROOT/src/web/sama-web"
STAGE="${SAMA_WEB_TEST_DIR:-${TMPDIR:-/tmp}/sama-web-tests}"

# Only the parentheses are the problem; a clean checkout can run in place.
if [[ "$WEB" != *"("* && "$WEB" != *")"* ]]; then
  echo "Path is clean; running in place."
  cd "$WEB" && exec npx ng test --no-watch "$@"
fi

echo "Checkout path contains parentheses; staging at $STAGE"
rm -rf "$STAGE"
mkdir -p "$STAGE"

cp -r "$WEB/src" "$STAGE/src"
cp "$WEB"/*.json "$STAGE/" 2>/dev/null || true
cp "$WEB"/*.ts "$STAGE/" 2>/dev/null || true
ln -s "$WEB/node_modules" "$STAGE/node_modules"

cd "$STAGE"
npx ng test --no-watch "$@"
