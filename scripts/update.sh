#!/usr/bin/env bash
# Update the Shelfie stack on the VM: backup, new images, restart, check.
#
#   scripts/update.sh --check     show the running and the newest Part-DB version, change nothing
#   scripts/update.sh             back up, pull and restart everything; Part-DB stays on its version
#   scripts/update.sh 2.21.0      the same, and move Part-DB to that version first
#
# Part-DB is pinned (PARTDB_VERSION in deploy/.env, default in docker-compose.yml) so it never
# changes by accident; read the release notes before moving it. The capture API, Tailscale and
# cloudflared follow :latest. Database migrations run on Part-DB's start (DB_AUTOMIGRATE).
# The off-site copy goes to $SHELFIE_BACKUP_TARGET if set, as for the nightly backup.

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STACK="$ROOT/deploy"
TAILNET="${SHELFIE_TAILNET:-tailf98c37.ts.net}"
cd "$STACK"

current_partdb() {
  local v
  v="$(grep -E '^PARTDB_VERSION=' .env 2>/dev/null | tail -1 | cut -d= -f2- || true)"
  [[ -n "$v" ]] || v="$(sed -nE 's/.*part-db1:\$\{PARTDB_VERSION:-([^}]+)\}.*/\1/p' docker-compose.yml)"
  echo "$v"
}

latest_partdb() {
  curl -fsS --max-time 10 https://api.github.com/repos/Part-DB/Part-DB-server/releases/latest \
    | sed -nE 's/.*"tag_name": *"v?([^"]+)".*/\1/p' | head -1
}

current="$(current_partdb)"
latest="$(latest_partdb || true)"
echo "Part-DB: running $current, newest release ${latest:-unknown}"
if [[ "${1:-}" == "--check" ]]; then
  [[ -n "$latest" && "$latest" != "$current" ]] \
    && echo "Release notes: https://github.com/Part-DB/Part-DB-server/releases/tag/v$latest"
  exit 0
fi

target="${1:-$current}"
if [[ ! "$target" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must look like 2.21.0 (got '$target')" >&2
  exit 1
fi

echo "== Backup"
"$ROOT/scripts/backup.sh" "$STACK"

if [[ "$target" != "$current" ]]; then
  echo "== Part-DB $current → $target"
  if grep -qE '^PARTDB_VERSION=' .env; then
    sed -i -E "s/^PARTDB_VERSION=.*/PARTDB_VERSION=$target/" .env
  else
    printf '\n# Part-DB image version (scripts/update.sh)\nPARTDB_VERSION=%s\n' "$target" >> .env
  fi
fi

echo "== Pull and restart"
docker compose pull
docker compose up -d --remove-orphans

echo "== Check (Part-DB may need a minute for migrations)"
ok=1
for i in $(seq 1 24); do
  if curl -fsS -o /dev/null --max-time 10 "https://parts.$TAILNET/en/" \
     && curl -fsS -o /dev/null --max-time 10 "https://capture.$TAILNET/api/health"; then
    ok=0; break
  fi
  sleep 5
done
if [[ $ok -eq 0 ]]; then
  echo "Part-DB and the capture API answer. Done."
else
  echo "Something doesn't answer after 2 minutes. Look at: docker compose logs partdb --tail 50" >&2
  echo "To go back: set PARTDB_VERSION=$current in deploy/.env and restore the database from the newest" >&2
  echo "deploy/backups/*/app.db (migrations may have changed it), then docker compose up -d." >&2
  exit 1
fi
docker image prune -f >/dev/null
