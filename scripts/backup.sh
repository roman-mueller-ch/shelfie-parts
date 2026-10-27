#!/usr/bin/env bash
# Nightly Shelfie backup: a consistent SQLite snapshot plus uploads,
# copied off the host (e.g. to the Synology over Tailscale).
#
# Usage:  scripts/backup.sh [stack_dir] [rsync_target]
#   stack_dir     folder containing docker-compose.yml and data/  (default: /srv/shelfie/deploy)
#   rsync_target  e.g. roman@synology:/volume1/backup/shelfie     (default: $SHELFIE_BACKUP_TARGET)
#
# Cron example (03:15 every night):
#   15 3 * * * /srv/shelfie/scripts/backup.sh >> /var/log/shelfie-backup.log 2>&1

set -euo pipefail

STACK_DIR="${1:-/srv/shelfie/deploy}"
TARGET="${2:-${SHELFIE_BACKUP_TARGET:-}}"
KEEP_LOCAL="${SHELFIE_BACKUP_KEEP:-7}"

DATA="$STACK_DIR/data"
OUT_ROOT="$STACK_DIR/backups"
STAMP="$(date +%Y-%m-%d_%H%M%S)"
OUT="$OUT_ROOT/$STAMP"

mkdir -p "$OUT"

# 1. Consistent copy of the live database (safe while Part-DB is running).
sqlite3 "$DATA/partdb/db/app.db" ".backup '$OUT/app.db'"
sqlite3 "$OUT/app.db" "PRAGMA integrity_check;" | grep -qx ok

# 2. Attachments and images.
tar -C "$DATA/partdb" -czf "$OUT/files.tar.gz" uploads public_media

echo "$(date -Is) backup written to $OUT"

# 3. Keep the newest N local backups.
ls -1dt "$OUT_ROOT"/*/ | tail -n +"$((KEEP_LOCAL + 1))" | xargs -r rm -rf

# 4. Ship off-host.
if [[ -n "$TARGET" ]]; then
  rsync -a --delete "$OUT_ROOT/" "$TARGET/"
  echo "$(date -Is) synced to $TARGET"
else
  echo "$(date -Is) no rsync target set; backup kept locally only" >&2
fi
