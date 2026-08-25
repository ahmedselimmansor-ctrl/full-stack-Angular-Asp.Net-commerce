#!/bin/sh
# Applies each service's migrations in turn, stopping at the first failure.
#
# Each bundle connects as that service's own database role, because each role owns exactly one
# schema. Running them all through a single superuser-ish credential would work, but it would
# undo the isolation the schema-per-service layout exists to provide — and it is the schema
# owner, not a shared admin, that must own the tables it creates.
#
# -e matters: without it a failed migration would still exit zero, and the new pods would roll
# out against a schema that was never updated.
set -eu

apply() {
  service="$1"
  connection="$2"

  if [ -z "${connection}" ]; then
    echo "!! no connection string for ${service}; refusing to continue" >&2
    exit 1
  fi

  echo "==> applying migrations for ${service}"
  "./migrate-${service}" --connection "${connection}"
  echo "==> ${service} up to date"
}

# Identity first: every other service validates the tokens it issues, so if a later bundle
# fails, a fully-migrated identity schema is the least disruptive state to be left in.
apply Identity  "${SAMA_DB_IDENTITY:-}"
apply Catalog   "${SAMA_DB_CATALOG:-}"
apply Ordering  "${SAMA_DB_ORDERING:-}"
apply Inventory "${SAMA_DB_INVENTORY:-}"
apply Payment   "${SAMA_DB_PAYMENT:-}"

echo "All migrations applied."
