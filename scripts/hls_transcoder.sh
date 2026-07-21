#!/bin/bash
# /usr/local/bin/hls_transcoder.sh
#
# Один ffmpeg: один декод RTMP → 4 scale → 4 muxed HLS variant (v:N + a:N каждый свой).
# Синхронные keyframe; без agroup (совместимость с hls.js).

STREAM_KEY="${1:?stream key required}"
BASE="/var/www/streamplatform/live/$STREAM_KEY"
LOG_DIR="/var/log/streamplatform"
LOG_FILE="$LOG_DIR/transcoder.log"
RTMP_URL="rtmp://127.0.0.1/live/$STREAM_KEY"
PGREP_PATTERN="ffmpeg.*${STREAM_KEY}"

HLS_TIME=2
FPS=30
GOP=$((FPS * HLS_TIME))                   # IDR каждые 2s @ 30fps
MAX_PLAYLIST_AGE=$((HLS_TIME * 2 + 1))   # плейлист не обновлялся → RTMP мёртв
WARMUP_SEC=20                             # exec_push retry пока ffmpeg стартует

mkdir -p "$LOG_DIR"

log() {
  echo "$(date -Is) [$STREAM_KEY] $*" >> "$LOG_FILE" 2>/dev/null || true
}

clean_hls_output() {
  log "cleaning HLS output (prevent mixed GOP after reconnect)"
  rm -rf "$BASE"
  # umask 002 + 2775: variant dirs stay group-writable so backend (www-data group) can clean on ban
  umask 002
  mkdir -p "$BASE"
  chmod 2775 "$BASE" 2>/dev/null || true
}

ffmpeg_pid() {
  pgrep -f "$PGREP_PATTERN" 2>/dev/null | head -1
}

ffmpeg_process_age_sec() {
  local pid start now
  pid=$(ffmpeg_pid)
  [[ -n "$pid" ]] || return 1
  start=$(stat -c %Y "/proc/$pid" 2>/dev/null || echo 0)
  now=$(date +%s)
  echo $(( now - start ))
}

playlist_age_sec() {
  local now mtime
  [[ -f "$BASE/720p/index.m3u8" ]] || return 999
  now=$(date +%s)
  mtime=$(stat -c %Y "$BASE/720p/index.m3u8" 2>/dev/null || echo 0)
  echo $(( now - mtime ))
}

ffmpeg_is_healthy() {
  local pid state age

  pid=$(ffmpeg_pid)
  [[ -n "$pid" ]] || return 1

  state=$(ps -o state= -p "$pid" 2>/dev/null | tr -d ' ')
  [[ "$state" == "R" || "$state" == "S" ]] || return 1

  [[ -f "$BASE/master.m3u8" && -f "$BASE/720p/index.m3u8" ]] || return 1

  age=$(playlist_age_sec)
  (( age <= MAX_PLAYLIST_AGE ))
}

log "transcoder start"

# 1) Живой transcode — не трогаем (reconnect / повторный exec_push)
if ffmpeg_is_healthy; then
  log "ffmpeg healthy, exit"
  exit 0
fi

# 2) Процесс есть, но ещё прогревается — не убивать (nginx часто шлёт exec_push повторно)
if pid=$(ffmpeg_pid) && [[ -n "$pid" ]]; then
  age=$(ffmpeg_process_age_sec)
  pl_age=$(playlist_age_sec)

  if (( age < WARMUP_SEC )) || (( pl_age <= MAX_PLAYLIST_AGE )); then
    log "ffmpeg running (age=${age}s, playlist_age=${pl_age}s), exit"
    exit 0
  fi

  log "ffmpeg stale (age=${age}s, playlist_age=${pl_age}s), killing"
  pkill -9 -f "$PGREP_PATTERN" 2>/dev/null || true
  sleep 1
fi

# 3) Чистый старт
clean_hls_output

sleep 2
log "starting ffmpeg; GOP=${GOP} frames, segment=${HLS_TIME}s, fps=${FPS}"

# independent_segments: каждый .ts начинается с IDR (важно против артефактов).
# split_by_time НЕ использовать — ffmpeg отключает independent_segments (см. warning в логе).
exec /usr/bin/ffmpeg -hide_banner -loglevel warning \
  -rw_timeout 5000000 \
  -fflags +genpts+discardcorrupt \
  -i "$RTMP_URL" \
  -filter_complex "[0:v]fps=${FPS},split=4[va][vb][vc][vd];[va]scale=1920:1080[v1080];[vb]scale=1280:720[v720];[vc]scale=854:480[v480];[vd]scale=426:240[v240]" \
  -map "[v1080]" -map 0:a? \
  -map "[v720]"  -map 0:a? \
  -map "[v480]"  -map 0:a? \
  -map "[v240]"  -map 0:a? \
  -c:v libx264 -preset veryfast -pix_fmt yuv420p \
  -g "$GOP" -keyint_min "$GOP" -sc_threshold 0 \
  -force_key_frames "expr:gte(t,n_forced*${HLS_TIME})" \
  -x264-params "scenecut=0:repeat-headers=1" \
  -b:v:0 4500k -maxrate:v:0 5000k -bufsize:v:0 6750k \
  -b:v:1 2500k -maxrate:v:1 2675k -bufsize:v:1 3750k \
  -b:v:2 1500k -maxrate:v:2 1600k -bufsize:v:2 2250k \
  -b:v:3 400k  -maxrate:v:3 500k  -bufsize:v:3 600k  \
  -c:a aac -b:a 128k -ar 44100 -ac 2 \
  -f hls -hls_time "$HLS_TIME" -hls_list_size 10 \
  -hls_flags delete_segments+temp_file+independent_segments+omit_endlist \
  -master_pl_name master.m3u8 \
  -var_stream_map "v:0,a:0,name:1080p,default:yes v:1,a:1,name:720p v:2,a:2,name:480p v:3,a:3,name:240p" \
  -hls_segment_filename "$BASE/%v/segment_%05d.ts" \
  "$BASE/%v/index.m3u8" \
  2>>"$LOG_FILE"

