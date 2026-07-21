#!/bin/bash
# Kill ffmpeg HLS transcoder for a stream key and remove its live HLS tree.
# Intended to run as root (via sudoers) or as the same user that owns ffmpeg (www-data).
set -euo pipefail

STREAM_KEY="${1:-}"
if [[ ! "$STREAM_KEY" =~ ^[A-Za-z0-9_-]+$ ]]; then
  echo "usage: $0 <streamKey>" >&2
  exit 2
fi

pkill -f "ffmpeg.*${STREAM_KEY}" 2>/dev/null || true
# give ffmpeg a moment to release files
sleep 0.3
rm -rf "/var/www/streamplatform/live/${STREAM_KEY}" 2>/dev/null || true
exit 0
