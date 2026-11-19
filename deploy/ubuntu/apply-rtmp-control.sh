#!/bin/bash
# Installs localhost-only nginx-rtmp /control (drop publisher) from the repo template.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NGINX_CONF="$SCRIPT_DIR/nginx.conf"

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Запусти с sudo: sudo bash $0"
  exit 1
fi

if ! grep -q 'rtmp_control' "$NGINX_CONF"; then
  echo "В шаблоне нет rtmp_control — проверь $NGINX_CONF"
  exit 1
fi

echo "==> Копируем nginx.conf"
cp "$NGINX_CONF" /etc/nginx/nginx.conf
if [[ -d /var/www/CopySysFiles ]]; then
  cp "$NGINX_CONF" /var/www/CopySysFiles/copy_nginx.conf || true
fi

echo "==> nginx -t"
nginx -t

echo "==> reload nginx"
systemctl reload nginx

echo "==> probe control"
code=$(curl -s -o /tmp/rtmp_control_probe -w '%{http_code}' --max-time 2 \
  'http://127.0.0.1:5155/control/drop/publisher?app=live&name=live_apply_probe' || echo x)
echo "HTTP $code body=$(cat /tmp/rtmp_control_probe)"
if [[ "$code" != "200" ]]; then
  echo "Ожидался HTTP 200 от /control — проверь location на 127.0.0.1:5155"
  exit 1
fi

echo "Готово."
