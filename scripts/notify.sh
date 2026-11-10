#!/usr/bin/env bash
# Push notification through ntfy (the homelab's server), like ~/bin/notify.sh on the Pi.
#
#   scripts/notify.sh "Title" "Message" [priority] [tags] [click-url]
#     priority  min | low | default | high | urgent   (default: default)
#     tags      ntfy emoji tags, e.g. white_check_mark, warning
#
# Settings in deploy/.env (or the environment):
#   NTFY_URL    e.g. https://ntfy.roman-mueller.ch
#   NTFY_TOPIC  e.g. homelab
# Access token (a write-only ntfy user for this VM) in ~/.config/shelfie/ntfy-token, chmod 600,
# or $NTFY_TOKEN_FILE. Without NTFY_URL the message only goes to stdout, so scripts still run.

set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ENV_FILE="$ROOT/deploy/.env"

from_env() { [[ -f "$ENV_FILE" ]] && sed -nE "s/^$1=//p" "$ENV_FILE" | tail -1 | tr -d "\"'"; }

URL="${NTFY_URL:-$(from_env NTFY_URL)}"
TOPIC="${NTFY_TOPIC:-$(from_env NTFY_TOPIC)}"
TOKEN_FILE="${NTFY_TOKEN_FILE:-$HOME/.config/shelfie/ntfy-token}"

title="${1:?title}"; message="${2:?message}"
priority="${3:-default}"; tags="${4:-}"; click="${5:-}"

echo "[notify] $title: $message"
[[ -n "$URL" && -n "$TOPIC" ]] || exit 0

headers=(-H "Title: $title" -H "Priority: $priority")
[[ -n "$tags" ]] && headers+=(-H "Tags: $tags")
[[ -n "$click" ]] && headers+=(-H "Click: $click")
[[ -r "$TOKEN_FILE" ]] && headers+=(-H "Authorization: Bearer $(cat "$TOKEN_FILE")")

curl -fsS --max-time 15 "${headers[@]}" -d "$message" "${URL%/}/$TOPIC" >/dev/null \
  || echo "[notify] sending to $URL/$TOPIC failed" >&2
