# Platform moderation & support plan

План общей (платформенной) модерации и службы поддержки.  
Канальная модерация (чат/команда стримера) остаётся отдельным слоем и не заменяется этим планом.

**Статус обновляйте чекбоксами.** Связанный live-roadmap: `LIVE_PLATFORM_ROADMAP.md`.

---

## Принципы

1. Один staff-слой, **две очереди**: T&S (репорты/санкции) и Support (тикеты).
2. Канал автономен; платформа вмешивается по жалобе / политике / критическому риску.
3. Саппорт и T&S делят аудит и карточку пользователя, но не смешивают очередь.
4. Platform-санкции — отдельная сущность, не `StreamChatBan`.
5. В v1: без AI-автобанов, без Zendesk-клона, без IP-ban как основы.

### Роли (глобальные)

| Роль | Зона |
|------|------|
| `Support` | тикеты, просмотр, сброс stream key (с аудитом), **без** банов |
| `Moderator` | platform T&S: репорты, mute/ban, end stream, hide channel |
| `Admin` / `SuperAdmin` | роли, перманент, полный доступ |

`Moderator` здесь = platform moderator (не путать с `StreamModerator` команды канала).

---

## Фаза 0 — фундамент

- [x] JWT: роль в `ClaimTypes.Role` (чтобы работал `IsInRole` / `[Authorize(Roles=...)]`)
- [x] Staff-роли: `Support`, `Moderator`, `Admin`, `SuperAdmin` + helper прав
- [x] Закрыть `/api/staff/*` авторизацией staff
- [x] `StaffAuditLog` + сервис записи аудита
- [x] Минимальный staff endpoint (`GET /api/staff/me`)

**Критерий:** staff проходит в `/api/staff/*`, обычный User — 403.

---

## Фаза 1 — санкции платформы

- [x] Модель `PlatformSanction` (warning / chat_mute / stream_ban / login_ban / full_ban)
- [x] Enforcement: логин, start stream, чат везде
- [x] Staff API выдать/снять санкцию + audit

**Критерий:** ручной mute/ban реально блокирует действие.

API (Moderator+):

- `GET /api/staff/sanctions` — активные
- `GET /api/staff/sanctions/user/{userId}`
- `POST /api/staff/sanctions` — body: `{ targetUserId, type, reason, durationMinutes? }`
- `POST /api/staff/sanctions/{id}/revoke` — body: `{ reason? }`

Типы: `warning`, `chat_mute`, `stream_ban`, `login_ban`, `full_ban`.  
`durationMinutes` null/0 = бессрочно.

---

## Фаза 2 — репорты (T&S)

- [x] Модель `Report` + статусы + assignee
- [x] Кнопки жалобы: чат / канал
- [x] Staff UI `/staff/reports`
- [x] Антиспам репортов

**Критерий:** жалоба → очередь → mute → нарушитель не пишет.

API:

- `POST /api/reports` — любой авторизованный
- `GET /api/staff/reports?status=` — Moderator+
- `PUT /api/staff/reports/{id}` — статус / резолюция / опциональная санкция

UI:

- чат: «Пожаловаться» в меню сообщения
- канал: кнопка на баннере
- `/staff/reports` + пункт Staff в меню аватара

---

## Фаза 3 — саппорт-тикеты

- [ ] `Ticket` + `TicketMessage`
- [ ] Категории + эскалация abuse → Report
- [ ] UI пользователя «Написать в поддержку»
- [ ] Staff UI `/staff/tickets`
- [ ] Support без права банить

**Критерий:** тикет про ключ → Support сбрасывает ключ с аудитом.

---

## Фаза 4 — staff-панель

- [ ] Оболочка `/staff` (reports / tickets / user search / audit)
- [ ] Назначение ролей (Admin)
- [ ] Фильтры и подсветка «протухших» заявок

---

## Фаза 5 — апелляции и полировка

- [ ] Апелляции только для platform-санкций
- [ ] Автоистечение temporary sanctions
- [ ] Уведомления пользователю
- [ ] Базовая статистика очередей

---

## Порядок релиза

```text
0 → 1 → 2 → 3 → 4 → 5
```

После фазы 1 уже можно модерировать вручную через API.  
После фазы 2 — полноценный T&S. Саппорт (3) можно релизить отдельно.

## Откат

Точки перед фазой 0 (уже на `origin/cursor-edited`):

- Backend: `2382fe1` — до staff foundation
- Frontend: `6bf4e00` — duration badge (можно откатываться независимо)

Фаза 0 закоммичена: backend `a6b1514`.
Миграция `Add_StaffAuditLog` применена к БД.
Чтобы эндпоинты заработали на сервере: `sudo systemctl restart streamplatform-backend`.
Для проверки `GET /api/staff/me` нужен пользователь с `Role` = `Support|Moderator|Admin|SuperAdmin` и **новый логин** (свежий JWT).
