#!/bin/bash
# Apply deploy/ubuntu/nginx.conf (RTMP callback proxy without ?secret= in URL).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NGINX_CONF="$SCRIPT_DIR/nginx.conf"
COPY_CONF="/var/www/CopySysFiles/copy_nginx.conf"

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Запусти с sudo: sudo bash $0"
  exit 1
fi

if ! grep -q 'listen 127.0.0.1:5155;' "$NGINX_CONF"; then
  echo "В $NGINX_CONF нет localhost proxy :5155 — проверь репозиторий."
  exit 1
fi

if grep -E 'on_publish .*\?secret=' "$NGINX_CONF"; then
  echo "В шаблоне всё ещё ?secret= в on_publish — отказ."
  exit 1
fi

echo "==> Копируем nginx.conf"
cp "$NGINX_CONF" /etc/nginx/nginx.conf
cp "$NGINX_CONF" "$COPY_CONF" 2>/dev/null || echo "(CopySysFiles недоступен — пропускаем)"

if [[ ! -f /etc/nginx/snippets/rtmp-callback-secret.conf ]]; then
  echo "==> Создаём snippet из example (затем sync-rtmp-secret-from-local.sh)"
  install -d -m 755 /etc/nginx/snippets
  cp "$SCRIPT_DIR/rtmp-callback-secret.conf.example" /etc/nginx/snippets/rtmp-callback-secret.conf
  chown root:www-data /etc/nginx/snippets/rtmp-callback-secret.conf
  chmod 640 /etc/nginx/snippets/rtmp-callback-secret.conf
fi

echo "==> nginx -t"
nginx -t

echo "==> reload nginx"
systemctl reload nginx

echo "==> проверка"
ss -ltn | grep -E ':5155|:5156' || true
grep -nE 'on_publish|5155|rtmp-callback-secret' /etc/nginx/nginx.conf | head -20
echo "Готово. Синхронизируй секрет: sudo bash $SCRIPT_DIR/sync-rtmp-secret-from-local.sh"
