# Справка: конфиги Ubuntu-сервера

Копии «как сейчас на машине». Обновляйте вручную после правок на сервере.

| Файл в репо | Путь на сервере |
|-------------|-----------------|
| `nginx.conf` | `/etc/nginx/nginx.conf` |
| `streamplatform-backend.service` | `/etc/systemd/system/streamplatform-backend.service` |
| `hls_transcoder.sh` | `/usr/local/bin/hls_transcoder.sh` |

Опционально (когда появится SRS): `srs.conf`, `srs-docker-compose.yml`.

## Логи nginx

В `nginx.conf` уровень `error_log` — **warn** (не `debug`: debug раздувает `error.log` до гигабайт на live HLS).

Применить на сервере и освободить место (~7 ГБ):

```bash
sudo bash /var/www/StreamPlatformBackend/deploy/ubuntu/fix-nginx-logs.sh
```

Скрипт: копирует конфиг в `/etc/nginx/` и `CopySysFiles/`, очищает `error.log`, reload nginx, при необходимости `logrotate rotate 7`.
