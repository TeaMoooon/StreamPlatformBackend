# Фаза 1.1 — Ingest (SRS)

Пошаговая настройка приёма RTMP через **SRS** вместо nginx-rtmp.

## Что получится

```text
OBS  --RTMP-->  SRS :1935  --HTTP hook-->  .NET :5156  (StartStream / EndStream)
                      |
                      +-- (позже) Transcode worker читает rtmp://127.0.0.1/live/{key}
```

nginx остаётся для HTTPS (React, `/api/`, `/hls/`), **без** блока `rtmp` на порту 1935.

---

## Шаг 0. Подготовка на сервере

```bash
# API должен быть запущен и слушать 5156
curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:5156/swagger/index.html

# Папки для HLS (transcode worker — фаза 1.2)
sudo mkdir -p /var/www/streamplatform/live
sudo chown -R www-data:www-data /var/www/streamplatform
```

Убедитесь, что PostgreSQL и Redis доступны API.

---

## Шаг 1. Установить Docker (если ещё нет)

```bash
sudo apt update
sudo apt install -y docker.io docker-compose-v2
sudo systemctl enable --now docker
```

---

## Шаг 2. Запустить SRS из репозитория

На сервере склонировать/обновить репозиторий, затем:

```bash
cd /path/to/StreamPlatformBackend/deploy/ubuntu
# Секрет в srs.conf = Rtmp:Secret в appsettings
docker compose -f srs-docker-compose.yml up -d
docker compose -f srs-docker-compose.yml logs -f srs
```

Проверка:

```bash
curl http://127.0.0.1:1985/api/v1/summaries
```

---

## Шаг 3. Отключить nginx-rtmp (конфликт порта 1935)

В `/etc/nginx/nginx.conf` **удалите или закомментируйте** весь блок `rtmp { ... }`.

```bash
sudo nginx -t
sudo systemctl reload nginx
```

Порт 1935 должен слушать только SRS:

```bash
sudo ss -tlnp | grep 1935
```

---

## Шаг 4. Firewall

```bash
sudo ufw allow 1935/tcp   # RTMP для OBS
# 1985, 8080 — только localhost, наружу не открывать
```

---

## Шаг 5. OBS

| Поле | Значение |
|------|----------|
| **Server** | `rtmp://IP_СЕРВЕРА/live` |
| **Stream Key** | `live_5_a1b2c3...` (из API / БД) |

Не используйте `rtmps://` и не кладите ключ в URL сервера.

---

## Шаг 6. Проверка колбэка

Подставьте реальный ключ из таблицы `Users`:

```bash
# nginx-формат (form)
curl -X POST "http://127.0.0.1:5156/api/streamcallback/start?secret=your-secret-value" \
  -d "name=live_USERID_GUID"

# SRS-формат (JSON)
curl -X POST "http://127.0.0.1:5156/api/streamcallback/start?secret=your-secret-value" \
  -H "Content-Type: application/json" \
  -d '{"app":"live","stream":"live_USERID_GUID"}'
```

Ожидается **HTTP 200**. В логах API: `=== STREAM START CALLBACK ===`.

После OBS:

```bash
docker compose -f deploy/ubuntu/srs-docker-compose.yml logs srs | tail -50
```

---

## Шаг 7. Внутренний pull для ffmpeg (фаза 1.2)

Worker будет читать:

```text
rtmp://127.0.0.1:1935/live/{stream_key}
```

Пока transcode worker не запущен — **картинки в `/hls/` не будет**, это нормально для чистого 1.1.

---

## Частые проблемы

| Симптом | Решение |
|---------|---------|
| OBS «Failed to connect» | SRS не запущен, firewall, nginx всё ещё на 1935 |
| OBS connect → сразу disconnect | API вернул не 200 на `on_publish` (ключ, секрет, БД) |
| 401 Unauthorized | `secret` в `srs.conf` ≠ `Rtmp:Secret` в appsettings |
| 500 на start | ключ не в БД или не совпадает с `Users.StreamKey` |
| Два процесса на 1935 | убрать `rtmp {}` из nginx |

---

## Чеклист фазы 1.1

- [ ] SRS запущен (`docker compose ps`)
- [ ] nginx `rtmp` отключён
- [ ] `curl` колбэка → 200
- [ ] OBS publish → лог API `STREAM START`
- [ ] OBS stop → лог API `STREAM END`
- [ ] В БД: `IsOnline`, `CurrentStream` корректны

После этого → **фаза 1.2 Transcode Worker**.
