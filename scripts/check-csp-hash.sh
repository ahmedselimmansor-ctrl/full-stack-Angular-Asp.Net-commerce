#!/usr/bin/env bash
# Verifies the CSP script hash still matches the inline script in index.html.
#
# index.html carries a small inline script that applies the saved theme and
# language before first paint, so a dark-mode visitor never sees a white flash.
# `script-src 'self'` would block it, so the policy pins its SHA-256 instead of
# opening the policy up with 'unsafe-inline'.
#
# The failure mode is quiet and only appears in production: edit the script,
# forget the hash, and the browser silently refuses to run it. Nothing fails
# locally, because the dev server sets no CSP at all.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INDEX="$ROOT/src/web/sama-web/src/index.html"
CONF="$ROOT/build/docker/nginx.conf"

ACTUAL=$(python3 - "$INDEX" <<'PY'
import base64, hashlib, re, sys

html = open(sys.argv[1], encoding="utf-8").read()
scripts = re.findall(r"<script>(.*?)</script>", html, re.S)

if len(scripts) != 1:
    sys.exit(f"expected exactly one inline script in index.html, found {len(scripts)}")

print("sha256-" + base64.b64encode(hashlib.sha256(scripts[0].encode()).digest()).decode())
PY
)

if grep -qF "'$ACTUAL'" "$CONF"; then
  echo "CSP hash matches the inline script ($ACTUAL)"
  exit 0
fi

CURRENT=$(grep -oE "'sha256-[A-Za-z0-9+/=]+'" "$CONF" | head -1)

cat >&2 <<MSG
CSP hash is stale.

  nginx.conf pins: ${CURRENT:-<none>}
  index.html needs: '$ACTUAL'

The inline theme script would be blocked in production while working fine
locally, because the dev server sets no CSP. Update the hash in
build/docker/nginx.conf, or re-run with --fix to do it automatically.
MSG

if [ "${1:-}" = "--fix" ]; then
  python3 - "$CONF" "$ACTUAL" <<'PY'
import re, sys
path, wanted = sys.argv[1], sys.argv[2]
text = open(path, encoding="utf-8").read()
open(path, "w", encoding="utf-8").write(
    re.sub(r"'sha256-[A-Za-z0-9+/=]+'", f"'{wanted}'", text, count=1))
PY
  echo "Updated $CONF"
  exit 0
fi

exit 1
