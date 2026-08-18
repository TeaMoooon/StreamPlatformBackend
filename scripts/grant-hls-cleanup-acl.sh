#!/bin/bash
# Grant the backend user write access to HLS output written by nginx/ffmpeg.
# Preferred: add boxedstream to www-data so unlink works without chmod.
# Optional ACL if the filesystem supports it (may require root).
set -euo pipefail

LIVE_BASE="/var/www/streamplatform/live"
USER_NAME="${1:-boxedstream}"

mkdir -p "$LIVE_BASE"
if getent group www-data >/dev/null; then
  usermod -aG www-data "$USER_NAME" 2>/dev/null || true
fi
if command -v setfacl >/dev/null; then
  setfacl -R -m "u:${USER_NAME}:rwx" "$LIVE_BASE" || true
  setfacl -R -d -m "u:${USER_NAME}:rwx" "$LIVE_BASE" || true
fi
echo "HLS cleanup access prepared for ${USER_NAME} on ${LIVE_BASE}"
