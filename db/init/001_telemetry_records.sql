-- SCADA Telemetry Collector: схема хранения временных рядов.
-- Скрипт идемпотентный: его выполняет и Postgres при первом старте контейнера
-- (docker-entrypoint-initdb.d), и API при запуске (Database:ApplySchemaOnStartup).

CREATE TABLE IF NOT EXISTS telemetry_records
(
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tag_id      integer          NOT NULL,
    "timestamp" timestamptz      NOT NULL,
    value       double precision NOT NULL,
    quality     integer          NOT NULL,
    CONSTRAINT ck_telemetry_records_quality CHECK (quality IN (0, 1))
);

-- Основной индекс для чтения истории по тегу за диапазон времени.
-- INCLUDE (value, quality) позволяет выполнять latest/history через Index Only Scan.
-- Обращения к heap всё ещё нужны для страниц без отметки all-visible в visibility map.
CREATE INDEX IF NOT EXISTS ix_telemetry_records_tag_id_timestamp
    ON telemetry_records (tag_id, "timestamp" DESC)
    INCLUDE (value, quality);

-- Таблица только дописывается (append-only). Чаще запускаем autovacuum по вставкам,
-- чтобы visibility map была актуальной и Index Only Scan не ходил в heap.
ALTER TABLE telemetry_records SET (
    autovacuum_vacuum_insert_scale_factor = 0.01,
    autovacuum_analyze_scale_factor = 0.02
);
