#!/bin/bash
# Remove HLS output for a publish key and optional PublicId playback symlink.
# Does NOT kill ffmpeg — use streamplatform-kill-hls-transcoder for that.
# Intended to run as root (via sudoers) because nginx/ffmpeg writes as nobody.
set -euo pipefail

LIVE_BASE="/var/www/streamplatform/live"
STREAM_KEY="${1:-}"
PLAYBACK_ID="${2:-}"

if [[ -z "$STREAM_KEY" && -z "$PLAYBACK_ID" ]]; then
  echo "usage: $0 <streamKey> [playbackId]" >&2
  exit 2
fi

if [[ -n "$STREAM_KEY" && ! "$STREAM_KEY" =~ ^[A-Za-z0-9_-]+$ ]]; then
  echo "invalid streamKey" >&2
  exit 2
fi

if [[ -n "$PLAYBACK_ID" && ! "$PLAYBACK_ID" =~ ^[A-Za-z0-9_-]+$ ]]; then
  echo "invalid playbackId" >&2
  exit 2
fi

if [[ -n "$PLAYBACK_ID" ]]; then
  link="${LIVE_BASE}/${PLAYBACK_ID}"
  if [[ -L "$link" ]]; then
    rm -f "$link"
  elif [[ -e "$link" ]]; then
    echo "refusing to delete non-symlink playback path: $link" >&2
    exit 3
  fi
fi

if [[ -n "$STREAM_KEY" ]]; then
  dir="${LIVE_BASE}/${STREAM_KEY}"
  if [[ -L "$dir" ]]; then
    rm -f "$dir"
  elif [[ -d "$dir" ]]; then
    rm -rf "$dir"
  fi
fi

exit 0
