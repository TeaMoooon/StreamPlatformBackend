# Справка: конфиги Ubuntu-сервера

Копии «как сейчас на машине». Обновляйте вручную после правок на сервере.

| Файл в репо | Путь на сервере |
|-------------|-----------------|
| `nginx.conf` | `/etc/nginx/nginx.conf` |
| `streamplatform-backend.service` | `/etc/systemd/system/streamplatform-backend.service` |
| `hls_transcoder.sh` | `/usr/local/bin/hls_transcoder.sh` |

Опционально (когда появится SRS): `srs.conf`, `srs-docker-compose.yml`.

## Секреты (appsettings.Local.json)

Секреты **только** в gitignored `StreamPlatformBackend/appsettings.Local.json` на диске
(`chmod 600`, владелец сервиса). Шаблон: `appsettings.Local.json.example`.

```bash
# создать/усилить Local + права 600
bash /var/www/StreamPlatformBackend/deploy/ubuntu/harden-local-secrets.sh

# синхронизировать Rtmp:Secret в nginx snippet (sudo)
sudo bash /var/www/StreamPlatformBackend/deploy/ubuntu/sync-rtmp-secret-from-local.sh

systemctl restart streamplatform-backend
```

Альтернатива: `deploy/ubuntu/backend.env` + systemd `EnvironmentFile=` (тоже gitignored).

## RTMP callback secret

Секрет **не** передаётся в query string (`?secret=`). nginx-rtmp/SRS бьют в
`http://127.0.0.1:5155/rtmp/on_publish` — локальный HTTP-прокси добавляет
`X-Rtmp-Secret` из `/etc/nginx/snippets/rtmp-callback-secret.conf`
(= `Rtmp:Secret` в Local).

Применить nginx-шаблон:

```bash
sudo bash /var/www/StreamPlatformBackend/deploy/ubuntu/apply-rtmp-callback-auth.sh
sudo bash /var/www/StreamPlatformBackend/deploy/ubuntu/sync-rtmp-secret-from-local.sh
```

Query `?secret=` больше не принимается API — только заголовок `X-Rtmp-Secret` через прокси `:5155`.

На том же `127.0.0.1:5155` есть `location /control` (`rtmp_control`) — бэкенд дергает
`Rtmp:DropPublisherUrl` при force-terminate / смене ключа, чтобы сбросить живой RTMP publisher.

После обновления шаблона nginx:

```bash
sudo cp /var/www/StreamPlatformBackend/deploy/ubuntu/nginx.conf /etc/nginx/nginx.conf
sudo nginx -t && sudo systemctl reload nginx
curl -s 'http://127.0.0.1:5155/control/drop/publisher?app=live&name=live_test'   # ожидается 200 и число
```

## Логи nginx

В `nginx.conf` уровень `error_log` — **warn** (не `debug`: debug раздувает `error.log` до гигабайт на live HLS).

Применить на сервере и освободить место (~7 ГБ):

```bash
sudo bash /var/www/StreamPlatformBackend/deploy/ubuntu/fix-nginx-logs.sh
```

Скрипт: копирует конфиг в `/etc/nginx/` и `CopySysFiles/`, очищает `error.log`, reload nginx, при необходимости `logrotate rotate 7`.
