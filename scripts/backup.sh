#!/usr/bin/env bash
# Nightly Shelfie backup: a consistent SQLite snapshot plus uploads,
# copied off the host (e.g. to the Synology over Tailscale).
#
# Usage:  scripts/backup.sh [stack_dir] [rsync_target]
#   stack_dir     folder containing docker-compose.yml and data/  (default: /srv/shelfie/deploy)
#   rsync_target  e.g. rsync://shelfie-backup@diskstation218.<tailnet>.ts.net/backup/shelfie
#                 (Synology rsync service, a DSM user with write access to that shared folder only)
#                 or user@host:/path over SSH. Default: $SHELFIE_BACKUP_TARGET, else
#                 SHELFIE_BACKUP_TARGET= in <stack_dir>/.env, so cron and update.sh find it too.
#   For rsync:// the password is read from ~/.config/shelfie/rsync.pass (chmod 600),
#   or $SHELFIE_BACKUP_PASSWORD_FILE.
#
# Cron example (03:15 every night):
#   15 3 * * * /srv/shelfie/scripts/backup.sh >> /srv/shelfie/backup.log 2>&1

set -euo pipefail

STACK_DIR="${1:-/srv/shelfie/deploy}"
TARGET="${2:-${SHELFIE_BACKUP_TARGET:-}}"
if [[ -z "$TARGET" && -f "$STACK_DIR/.env" ]]; then
  TARGET="$(sed -nE 's/^SHELFIE_BACKUP_TARGET=//p' "$STACK_DIR/.env" | tail -1 | tr -d "\"'")"
fi
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

# 4. Ship off-host. No --delete: the Synology keeps more than the VM and applies its own
#    retention (10 newest + one per week for 6 months, prune-backups.sh in DSM Task Scheduler).
if [[ -n "$TARGET" ]]; then
  RSYNC_OPTS=(-a)
  if [[ "$TARGET" == rsync://* ]]; then
    PASS_FILE="${SHELFIE_BACKUP_PASSWORD_FILE:-$HOME/.config/shelfie/rsync.pass}"
    [[ -r "$PASS_FILE" ]] || { echo "$(date -Is) missing rsync password file $PASS_FILE" >&2; exit 1; }
    # DSM's rsync modules don't keep Unix owners/permissions anyway.
    RSYNC_OPTS=(-rlt --password-file="$PASS_FILE")
  fi
  rsync "${RSYNC_OPTS[@]}" "$OUT_ROOT/" "$TARGET/"
  echo "$(date -Is) synced to $TARGET"
else
  echo "$(date -Is) no rsync target set; backup kept locally only" >&2
fi
