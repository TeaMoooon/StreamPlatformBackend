#!/bin/bash
# Sync Rtmp:Secret from appsettings.Local.json → nginx snippet (no secret in query / main nginx.conf).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOCAL_JSON="${LOCAL_JSON:-/var/www/StreamPlatformBackend/StreamPlatformBackend/appsettings.Local.json}"
SNIPPET_DST="/etc/nginx/snippets/rtmp-callback-secret.conf"
NGINX_CONF="$SCRIPT_DIR/nginx.conf"

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Запусти с sudo: sudo bash $0"
  exit 1
fi

if [[ ! -f "$LOCAL_JSON" ]]; then
  echo "Нет $LOCAL_JSON — скопируй из appsettings.Local.json.example"
  exit 1
fi

export LOCAL_JSON
SECRET="$(python3 - <<'PY'
import json
import os
from pathlib import Path

p = Path(os.environ["LOCAL_JSON"])
d = json.loads(p.read_text())
s = (d.get("Rtmp") or {}).get("Secret") or ""
if not s.strip() or "CHANGE_ME" in s or s.strip() == "your-secret-value":
    raise SystemExit("Rtmp:Secret missing or placeholder in Local.json")
# nginx string safety: reject quotes/newlines/backslashes
forbidden = {'"', "'", "\n", "\r", "\\"}
if any(c in s for c in forbidden):
    raise SystemExit("Rtmp:Secret contains unsafe characters for nginx snippet")
print(s, end="")
PY
)"

umask 077
install -d -m 755 /etc/nginx/snippets
# Write snippet via python to avoid shell expansion of secret
export SNIPPET_DST SECRET
python3 - <<'PY'
import os
from pathlib import Path

dst = Path(os.environ["SNIPPET_DST"])
secret = os.environ["SECRET"]
dst.write_text(
    "# Generated from appsettings.Local.json — do not commit. chmod 640 root:www-data\n"
    f'set $rtmp_callback_secret "{secret}";\n'
)
PY
unset SECRET
chown root:www-data "$SNIPPET_DST"
chmod 640 "$SNIPPET_DST"

# Ensure main nginx.conf uses the snippet (from repo template)
if ! grep -q 'rtmp-callback-secret.conf' /etc/nginx/nginx.conf; then
  echo "==> Updating /etc/nginx/nginx.conf from deploy template"
  cp "$NGINX_CONF" /etc/nginx/nginx.conf
fi

nginx -t
systemctl reload nginx
echo "Synced Rtmp secret → $SNIPPET_DST and reloaded nginx."
