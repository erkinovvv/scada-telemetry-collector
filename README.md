# SCADA Telemetry Collector

Бэкенд-сервис для приёма телеметрии с датчиков SCADA. Принимает замеры пачками, сохраняет их в PostgreSQL и отдаёт историю через HTTP API. В комплекте фоновый эмулятор на 100 тегов и операторский экран на React.

**Стек:** .NET 10 (C#), ASP.NET Core Minimal API, Npgsql, PostgreSQL 18, React + Vite, Docker Compose, xUnit + Testcontainers, k6.

## Быстрый старт

Нужен только Docker.

```bash
docker compose up --build
```

| Что | Адрес |
|---|---|
| Операторский экран (React) | http://localhost:3000 |
| API | http://localhost:8080 |
| OpenAPI-описание | http://localhost:8080/openapi/v1.json |
| Проверка здоровья | http://localhost:8080/health |
| PostgreSQL | `localhost:5432`, БД/пользователь/пароль `telemetry` |

Через пару секунд после старта эмулятор начинает писать по 100 замеров в секунду. На экране выберите тег, чтобы увидеть текущее значение и тренд.

Остановить: `docker compose down`. Удалить вместе с данными: `docker compose down -v`.

## API

| Метод | Путь | Описание |
|---|---|---|
| `POST` | `/api/telemetry/batch` | Пачка замеров (1–1000). Сохраняется одной атомарной операцией. Ответ: `{"inserted": N}` |
| `GET` | `/api/tags/{tagId}/latest` | Последний замер тега. `?goodOnly=true` пропускает недостоверные. Нет данных → `404` |
| `GET` | `/api/tags/{tagId}/history?from=&to=&limit=100` | Замеры за `[from, to)`, от новых к старым. По умолчанию `to` = сейчас, `from` = `to − 1 ч`, `limit` = 100 (максимум 10 000) |

Пример пачки:

```json
[
  { "tagId": 1, "timestamp": "2026-11-02T10:00:00Z", "value": 42.5, "quality": 1 },
  { "tagId": 2, "timestamp": "2026-11-02T10:00:00Z", "value": 17.1, "quality": 0 }
]
```

`quality`: `0` = Bad, `1` = Good. Время можно передавать с любым смещением, сервис хранит его в UTC. Ошибки валидации возвращаются как `400 ValidationProblem` с указанием записи и поля, например `"[3].quality"`. Готовые запросы для VS Code / Rider лежат в [`src/Scada.Telemetry.Api/Scada.Telemetry.Api.http`](src/Scada.Telemetry.Api/Scada.Telemetry.Api.http).

## Архитектура

```
src/
  Scada.Telemetry.Domain          модель замера (TelemetryRecord, Quality), без зависимостей
  Scada.Telemetry.Application     бизнес-логика: валидация, значения по умолчанию, ITelemetryRepository
  Scada.Telemetry.Infrastructure  PostgreSQL на Npgsql: репозиторий, инициализация схемы, health check
  Scada.Telemetry.Api             Minimal API, фоновый эмулятор, JSON, DI-композиция
tests/
  Scada.Telemetry.UnitTests         валидация, сервис, генератор сигналов
  Scada.Telemetry.IntegrationTests  репозиторий и HTTP API на настоящем PostgreSQL
db/init/001_telemetry_records.sql   схема: таблица и индексы
frontend/                           React-приложение и nginx
load/telemetry.js                   нагрузочный сценарий k6
```

Зависимости идут только внутрь: `Api → Application ← Infrastructure`, `Application → Domain`. API и эмулятор используют один и тот же `TelemetryService`, поэтому данные из обоих источников проходят одинаковую валидацию.

## Ключевые решения

**Запись: бинарный `COPY`.** Пачка уходит в PostgreSQL одной командой `COPY ... FROM STDIN (FORMAT BINARY)`. Команда атомарна: при ошибке в любой строке не сохраняется ни одна (это проверяет интеграционный тест). Цикла одиночных `INSERT` нет.

**Чтение: покрывающий индекс.** `(tag_id, "timestamp" DESC) INCLUDE (value, quality)`. Запросы `latest` и `history` выполняются как Index Only Scan и не обращаются к самой таблице. Для таблицы, которая только дописывается, понижен порог autovacuum по вставкам, чтобы visibility map оставалась свежей.

**Минимум накладных расходов на запрос.**
- `NpgsqlDataSource` — один пул соединений на приложение.
- Позиционные параметры `$1…`, автоматическая подготовка запросов (`Max Auto Prepare`).
- Чтение через `DataReader` по номеру колонки.
- JSON через source generator, без рефлексии.
- Замер — `readonly record struct`: пачка из 1000 элементов не создаёт 1000 объектов.
- Сервисы без состояния зарегистрированы как Singleton.

**Эмулятор.** `BackgroundService` с `PeriodicTimer`: тики не накладываются друг на друга, если БД отвечает медленно. Ошибка одной итерации логируется и не останавливает приложение. Отмена через `CancellationToken` обрабатывается штатно. Буфер пачки переиспользуется между тиками.

**Надёжный старт.** API дожидается PostgreSQL (повторы при временных ошибках) и применяет идемпотентный SQL-скрипт схемы под advisory lock. Тот же скрипт монтируется в `docker-entrypoint-initdb.d` контейнера Postgres.

**Защита от больших запросов.** Размер тела ограничен 1 МБ, пачка — 1000 записей, `limit` истории — 10 000. Число ошибок валидации в ответе ограничено 20.

## Результаты замеров

Замеры сделаны на одной машине (16 vCPU, 7 ГБ RAM): API в Release, PostgreSQL 16 и k6 работают одновременно, в таблице 5–11 млн строк. Абсолютные цифры зависят от железа, соотношения показательны.

**Нагрузочный тест** (`load/telemetry.js`, 60 с, параллельно с работающим эмулятором):

| Сценарий | Нагрузка | p50 | p95 | p99 |
|---|---|---|---|---|
| `POST /batch` | 50 пачек/с × 1000 = **50 000 строк/с** | 19,5 мс | 51 мс | 85 мс |
| `GET /latest` | 500 запросов/с | 2,1 мс | 17 мс | 32 мс |
| `GET /history` (15 мин, limit 100) | 100 запросов/с | 3,2 мс | 18 мс | 35 мс |

Ошибок: 0 из 39 003 запросов.

**Способ вставки** (1000 строк, таблица ~11 млн строк):

| Способ | Время |
|---|---|
| Одиночные `INSERT` в цикле (в одной транзакции) | 137 мс |
| `INSERT ... SELECT unnest(массивы)` | 11 мс |
| Бинарный `COPY` (используется) | 11 мс |

COPY и `unnest` на этом железе одинаково быстры. Узкое место здесь — обновление индексов и WAL, а не разбор SQL. COPY выбран потому, что он передаёт строки потоком, без огромных массивов в параметрах, и это штатный механизм массовой загрузки в PostgreSQL.

**Покрывающий индекс** (история за сутки, 10 000 строк, 5 млн строк в таблице):

| Индекс | Прочитано страниц | Время |
|---|---|---|
| `(tag_id, timestamp DESC)` | ~8 400 | 12,6 мс |
| `(tag_id, timestamp DESC) INCLUDE (value, quality)` | ~110 | 2,9 мс |

Запустить нагрузочный тест у себя:

```bash
docker compose up -d --build
docker compose --profile loadtest run --rm k6
```

## Тесты

```bash
dotnet test
```

Интеграционные тесты сами поднимают PostgreSQL в Docker через Testcontainers. Чтобы использовать уже запущенную БД, задайте строку подключения:

```bash
TEST_POSTGRES_CONNECTION="Host=localhost;Database=telemetry;Username=telemetry;Password=telemetry" dotnet test
```

Покрыто:
- валидация пачки;
- нормализация времени в UTC;
- атомарность COPY;
- семантика `latest` и `history` (полуинтервал, порядок, лимит);
- использование индекса;
- ответы HTTP API, включая ошибки;
- запись и штатная остановка эмулятора.

## Запуск без Docker

Нужны .NET 10 SDK, Node.js 22 и PostgreSQL.

```bash
# API на http://localhost:5080, строка подключения — в appsettings.json
dotnet run --project src/Scada.Telemetry.Api

# Фронтенд на http://localhost:5173 (проксирует /api на :5080)
cd frontend && npm ci && npm run dev
```

## Конфигурация

`src/Scada.Telemetry.Api/appsettings.json`. Любой параметр можно переопределить переменной окружения, например `Emulator__Enabled=false`.

| Секция | Параметры |
|---|---|
| `ConnectionStrings:Telemetry` | строка подключения и настройки пула |
| `Telemetry` | `MaxBatchSize`, `DefaultHistoryLimit`, `MaxHistoryLimit`, `DefaultHistoryWindow` |
| `Emulator` | `Enabled`, `TagCount`, `Interval`, `BadQualityRatio`, `Seed` |
| `Database` | `ApplySchemaOnStartup`, `StartupConnectAttempts`, `StartupConnectDelay` |

## Что дальше при росте нагрузки

Один эмулятор даёт ~8,6 млн строк в сутки. Следующие шаги:

1. **Партиционирование по времени** (по дням или неделям, нативно или через TimescaleDB). Индексы становятся маленькими и помещаются в память, а старые данные удаляются мгновенно (`DROP` партиции вместо `DELETE`). Для этого первичный ключ должен включать время: `(id, "timestamp")`.
2. **Retention и агрегаты.** Сырые данные хранятся N дней, для длинных периодов используются минутные и часовые агрегаты.
3. **Горизонтальное масштабирование API.** Сервис без состояния, инициализация схемы защищена advisory lock, поэтому можно запустить несколько экземпляров за балансировщиком. Для чтения можно подключить реплику.
4. **Буферизация приёма** (очередь в памяти или брокер). Отвечать клиенту до записи в БД здесь сознательно не стали: ответ `200` означает, что данные уже сохранены.
