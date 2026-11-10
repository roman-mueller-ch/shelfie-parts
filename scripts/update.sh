#!/usr/bin/env bash
# Update the Shelfie stack on the VM: backup, new images, restart, check.
#
#   scripts/update.sh --check     show the running and the newest Part-DB version, change nothing
#   scripts/update.sh             back up, pull and restart everything; Part-DB stays on its version
#   scripts/update.sh 2.21.0      the same, and move Part-DB to that version first
#   scripts/update.sh --auto      for cron: move Part-DB to the newest release within its major
#                                 version, report through ntfy (scripts/notify.sh). A new major
#                                 version is only announced; install it by hand after reading
#                                 the release notes.
#
# Part-DB is pinned (PARTDB_VERSION in deploy/.env, default in docker-compose.yml) so it never
# changes by accident. The capture API, Tailscale and cloudflared follow :latest. Database
# migrations run on Part-DB's start (DB_AUTOMIGRATE). The off-site copy goes to the Synology
# (SHELFIE_BACKUP_TARGET), as for the nightly backup.

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STACK="$ROOT/deploy"
TAILNET="${SHELFIE_TAILNET:-tailf98c37.ts.net}"
NOTIFY="$ROOT/scripts/notify.sh"
RELEASES=https://github.com/Part-DB/Part-DB-server/releases
cd "$STACK"

AUTO=0
step="start"
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
set_version() {
  if grep -qE '^PARTDB_VERSION=' .env; then
    sed -i -E "s/^PARTDB_VERSION=.*/PARTDB_VERSION=$1/" .env
  else
    printf '\n# Part-DB image version (scripts/update.sh)\nPARTDB_VERSION=%s\n' "$1" >> .env
  fi
}

current="$(current_partdb)"
latest="$(latest_partdb || true)"
[[ "$latest" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || latest=""
echo "Part-DB: running $current, newest release ${latest:-unknown}"

case "${1:-}" in
  --check)
    [[ -n "$latest" && "$latest" != "$current" ]] && echo "Release notes: $RELEASES/tag/v$latest"
    exit 0 ;;
  --auto)
    AUTO=1
    # Any failure from here on ends in one high-priority message naming the step.
    trap 'rc=$?; [[ $rc -ne 0 ]] && "$NOTIFY" "Part-DB update failed" "Step: $step. Still on ${current} unless the step was after the restart; details: docker compose logs partdb --tail 50 on shelfie-vm." high warning "$RELEASES"' EXIT
    if [[ -z "$latest" ]]; then
      step="checking GitHub for the newest release"; exit 1
    fi
    if [[ "$latest" == "$current" ]]; then
      "$NOTIFY" "Part-DB is up to date" "Running $current, the newest release." min heavy_check_mark
      exit 0
    fi
    if [[ "${latest%%.*}" != "${current%%.*}" ]]; then
      "$NOTIFY" "Part-DB $latest available" \
        "New major version ($current → $latest), not installed automatically. Read the release notes, then on shelfie-vm: /srv/shelfie/scripts/update.sh $latest" \
        default package "$RELEASES/tag/v$latest"
      exit 0
    fi
    target="$latest" ;;
  *)
    target="${1:-$current}" ;;
esac

if [[ ! "$target" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must look like 2.21.0 (got '$target')" >&2
  exit 1
fi

echo "== Backup"
step="backup"
"$ROOT/scripts/backup.sh" "$STACK"

if [[ "$target" != "$current" ]]; then
  echo "== Part-DB $current → $target"
  step="pulling the Part-DB $target image (maybe not published yet; the next run retries)"
  if ! PARTDB_VERSION="$target" docker compose pull partdb; then
    echo "Image for Part-DB $target not available (yet). Staying on $current." >&2
    exit 1
  fi
  set_version "$target"
fi

echo "== Pull and restart"
step="pulling images"
docker compose pull
step="restarting containers"
docker compose up -d --remove-orphans

echo "== Check (Part-DB may need a minute for migrations)"
step="health check after the update (backup of the old state in deploy/backups/)"
ok=1
for i in $(seq 1 24); do
  if curl -fs -o /dev/null --max-time 10 "https://parts.$TAILNET/en/" \
     && curl -fs -o /dev/null --max-time 10 "https://capture.$TAILNET/api/health"; then
    ok=0; break
  fi
  sleep 5   # a 502 while Part-DB starts is expected; stay quiet and retry
done
if [[ $ok -ne 0 ]]; then
  echo "Something doesn't answer after 2 minutes. Look at: docker compose logs partdb --tail 50" >&2
  echo "To go back: set PARTDB_VERSION=$current in deploy/.env and restore the database from the newest" >&2
  echo "deploy/backups/*/app.db (migrations may have changed it), then docker compose up -d." >&2
  exit 1
fi
echo "Part-DB and the capture API answer. Done."
docker image prune -f >/dev/null || true

if [[ $AUTO -eq 1 ]]; then
  "$NOTIFY" "Part-DB updated to $target" "Updated $current → $target. Changes: $RELEASES/tag/v$target" \
    default white_check_mark "$RELEASES/tag/v$target"
fi
