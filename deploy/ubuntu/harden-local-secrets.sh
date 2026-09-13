#!/bin/bash
# Harden appsettings.Local.json on disk: ensure exists, strong secrets, chmod 600.
set -euo pipefail

APP_DIR="${APP_DIR:-/var/www/StreamPlatformBackend/StreamPlatformBackend}"
LOCAL="$APP_DIR/appsettings.Local.json"
EXAMPLE="$APP_DIR/appsettings.Local.json.example"
OWNER="${LOCAL_OWNER:-boxedstream}"

if [[ ! -f "$EXAMPLE" ]]; then
  echo "Missing $EXAMPLE"
  exit 1
fi

python3 - <<PY
import json, secrets, string
from pathlib import Path

local_path = Path("$LOCAL")
example_path = Path("$EXAMPLE")

def strong(n=48):
    alphabet = string.ascii_letters + string.digits + "-_"
    return "".join(secrets.choice(alphabet) for _ in range(n))

def is_weak(val: str | None, kind: str) -> bool:
    if not val or not str(val).strip():
        return True
    s = str(val).strip()
    if "CHANGE_ME" in s:
        return True
    if s in (
        "your-secret-value",
        "your-super-secret-key-minimum-32-chars-long-here!",
    ):
        return True
    if kind == "jwt" and len(s) < 32:
        return True
    if kind == "rtmp" and len(s) < 16:
        return True
    return False

if local_path.exists():
    data = json.loads(local_path.read_text())
else:
    data = json.loads(example_path.read_text())

data.setdefault("ConnectionStrings", {})
data.setdefault("Jwt", {})
data.setdefault("Rtmp", {})

db = data["ConnectionStrings"].get("DefaultConnection")
if is_weak(db, "db"):
    raise SystemExit(
        "ConnectionStrings:DefaultConnection missing/placeholder. "
        "Set a real Postgres connection string in appsettings.Local.json first."
    )

if is_weak(data["Jwt"].get("SecretKey"), "jwt"):
    data["Jwt"]["SecretKey"] = strong(48)

if is_weak(data["Rtmp"].get("Secret"), "rtmp"):
    data["Rtmp"]["Secret"] = strong(32)

# Ensure Redis present
if not data["ConnectionStrings"].get("Redis"):
    data["ConnectionStrings"]["Redis"] = "localhost:6379,abortConnect=false"

local_path.write_text(json.dumps(data, indent=2) + "\n")
print(f"Wrote {local_path} (secrets not printed)")
print(f"Jwt rotated/ok len={len(data['Jwt']['SecretKey'])}")
print(f"Rtmp rotated/ok len={len(data['Rtmp']['Secret'])}")
PY

chown "$OWNER:$OWNER" "$LOCAL" 2>/dev/null || true
if [[ "$(id -u)" -eq 0 ]]; then
  chown "$OWNER:$OWNER" "$LOCAL"
fi
chmod 600 "$LOCAL"
# If we still own as root and OWNER differs, warn
if [[ "$(stat -c '%U' "$LOCAL")" != "$OWNER" ]]; then
  echo "WARN: owner is $(stat -c '%U:%G' "$LOCAL"), expected $OWNER:$OWNER (sudo chown needed)"
fi
stat -c '%a %U:%G %n' "$LOCAL"
echo "Done. Next (sudo): bash $(dirname "$0")/sync-rtmp-secret-from-local.sh && systemctl restart streamplatform-backend"
