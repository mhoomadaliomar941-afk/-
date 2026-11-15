#!/usr/bin/env bash
# Tests the migrations on a throwaway local Postgres database (no Supabase needed).
set -euo pipefail
cd "$(dirname "$0")/.."
DB=replica_backend_test
PSQL=(psql -qX -v ON_ERROR_STOP=1)
[ "$(id -u)" = 0 ] && PSQL=(su postgres -c) || true
run() { if [ "$(id -u)" = 0 ]; then su postgres -c "psql -qX -v ON_ERROR_STOP=1 $*"; else psql -qX -v ON_ERROR_STOP=1 "$@"; fi; }
run -c "'drop database if exists $DB'" -c "'create database $DB'"
for f in tests/stub_supabase.sql migrations/*.sql tests/backend_test.sql; do
  cp "$f" /tmp/replica_sql && chmod 644 /tmp/replica_sql
  run -d $DB -f /tmp/replica_sql
done
