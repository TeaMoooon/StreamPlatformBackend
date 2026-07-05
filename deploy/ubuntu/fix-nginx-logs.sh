#!/bin/bash
# Освобождает место: warn вместо debug + очистка раздутых error.log
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NGINX_CONF="$SCRIPT_DIR/nginx.conf"
COPY_CONF="/var/www/CopySysFiles/copy_nginx.conf"
LOGROTATE="/etc/logrotate.d/nginx"

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Запусти с sudo: sudo bash $0"
  exit 1
fi

if ! grep -q 'error_log /var/log/nginx/error.log warn;' "$NGINX_CONF"; then
  echo "Ожидается warn в $NGINX_CONF — проверь репозиторий."
  exit 1
fi

echo "==> Копируем nginx.conf на сервер и в CopySysFiles"
cp "$NGINX_CONF" /etc/nginx/nginx.conf
cp "$NGINX_CONF" "$COPY_CONF"

echo "==> Проверка конфига"
nginx -t

echo "==> Очистка error.log (~7 ГБ)"
truncate -s 0 /var/log/nginx/error.log
rm -f /var/log/nginx/error.log.1

if [[ -f "$LOGROTATE" ]] && grep -q 'rotate 14' "$LOGROTATE"; then
  echo "==> logrotate: rotate 14 -> 7 (экономия места на VM)"
  sed -i 's/rotate 14/rotate 7/' "$LOGROTATE"
fi

echo "==> reload nginx"
systemctl reload nginx

echo ""
echo "Готово. Место на диске:"
df -h /
echo ""
echo "error.log:"
ls -lh /var/log/nginx/error.log* 2>/dev/null | head -5
