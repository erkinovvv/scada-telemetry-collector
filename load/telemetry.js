// Нагрузочный тест k6: одновременно запись пачек и чтение latest/history.
// Запуск: docker compose --profile loadtest run --rm k6
// Параметры через переменные окружения: BASE_URL, INGEST_RPS, LATEST_RPS, HISTORY_RPS, DURATION, BATCH_SIZE.
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const DURATION = __ENV.DURATION || '1m';
const BATCH_SIZE = Number(__ENV.BATCH_SIZE || 1000);

// Теги 1001+ — чтобы нагрузка не смешивалась с данными эмулятора (теги 1..100).
const LOAD_TAG_BASE = 1000;
const LOAD_TAG_COUNT = 1000;

export const options = {
  scenarios: {
    ingest: {
      executor: 'constant-arrival-rate',
      exec: 'ingest',
      rate: Number(__ENV.INGEST_RPS || 50), // 50 пачек × 1000 = 50 000 строк/с
      timeUnit: '1s',
      duration: DURATION,
      preAllocatedVUs: 50,
      maxVUs: 200,
    },
    latest: {
      executor: 'constant-arrival-rate',
      exec: 'latest',
      rate: Number(__ENV.LATEST_RPS || 500),
      timeUnit: '1s',
      duration: DURATION,
      preAllocatedVUs: 50,
      maxVUs: 200,
    },
    history: {
      executor: 'constant-arrival-rate',
      exec: 'history',
      rate: Number(__ENV.HISTORY_RPS || 100),
      timeUnit: '1s',
      duration: DURATION,
      preAllocatedVUs: 20,
      maxVUs: 100,
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{scenario:ingest}': ['p(95)<500'],
    'http_req_duration{scenario:latest}': ['p(95)<100'],
    'http_req_duration{scenario:history}': ['p(95)<200'],
  },
};

const randomTag = () => LOAD_TAG_BASE + 1 + Math.floor(Math.random() * LOAD_TAG_COUNT);

export function ingest() {
  const now = Date.now();
  const batch = new Array(BATCH_SIZE);
  for (let i = 0; i < BATCH_SIZE; i++) {
    batch[i] = {
      tagId: LOAD_TAG_BASE + 1 + (i % LOAD_TAG_COUNT),
      timestamp: new Date(now - i).toISOString(),
      value: Math.random() * 1000,
      quality: Math.random() < 0.01 ? 0 : 1,
    };
  }

  const res = http.post(`${BASE_URL}/api/telemetry/batch`, JSON.stringify(batch), {
    headers: { 'Content-Type': 'application/json' },
    tags: { name: 'POST /api/telemetry/batch' },
  });
  check(res, { 'batch saved': (r) => r.status === 200 });
}

export function latest() {
  // 404 — нормальный ответ для тега, по которому ещё не пришло ни одного замера.
  const res = http.get(`${BASE_URL}/api/tags/${randomTag()}/latest`, {
    tags: { name: 'GET /api/tags/{id}/latest' },
    responseCallback: http.expectedStatuses(200, 404),
  });
  check(res, { 'latest ok': (r) => r.status === 200 || r.status === 404 });
}

export function history() {
  const to = new Date();
  const from = new Date(to.getTime() - 15 * 60 * 1000);
  const url = `${BASE_URL}/api/tags/${randomTag()}/history?from=${from.toISOString()}&to=${to.toISOString()}&limit=100`;
  const res = http.get(url, { tags: { name: 'GET /api/tags/{id}/history' } });
  check(res, { 'history ok': (r) => r.status === 200 });
}
