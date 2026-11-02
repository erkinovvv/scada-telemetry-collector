import assert from 'node:assert/strict';
import test from 'node:test';
import { getHistoryLimit, sampleTrendPoints } from '../src/trend.ts';

const start = Date.UTC(2026, 10, 2, 10);
const record = (second, value = second, quality = 1) => ({
  tagId: 1,
  timestamp: new Date(start + second * 1000).toISOString(),
  value,
  quality,
});

test('every selectable window can fetch its full one-second history', () => {
  for (const seconds of [300, 900, 3600]) {
    assert.ok(getHistoryLimit(seconds * 1000) > seconds);
    assert.ok(getHistoryLimit(seconds * 1000) <= 10_000);
  }
  assert.equal(getHistoryLimit(24 * 60 * 60_000), 10_000);
});

test('small histories stay complete, chronological and do not mutate API data', () => {
  const newest = record(2);
  const oldest = record(1);
  const input = Object.freeze([newest, oldest]);
  assert.deepEqual(sampleTrendPoints(input), [oldest, newest]);
  assert.deepEqual(input, [newest, oldest]);
  assert.deepEqual(sampleTrendPoints([]), []);
});

test('a full hour keeps both ends, peaks and bad quality while limiting SVG points', () => {
  const chronological = Array.from({ length: 3600 }, (_, i) => record(i, 10));
  chronological[1100] = record(1100, -100);
  chronological[2100] = record(2100, 200);
  chronological[2700] = record(2700, 10, 0);
  const sampled = sampleTrendPoints([...chronological].reverse());

  assert.ok(sampled.length <= 1000);
  assert.equal(sampled[0], chronological[0]);
  assert.equal(sampled.at(-1), chronological.at(-1));
  assert.ok(sampled.includes(chronological[1100]));
  assert.ok(sampled.includes(chronological[2100]));
  assert.ok(sampled.includes(chronological[2700]));
  for (let i = 1; i < sampled.length; i++) {
    assert.ok(sampled[i].timestamp > sampled[i - 1].timestamp);
  }
  for (let quarter = 0; quarter < 4; quarter++) {
    assert.ok(sampled.some((p) => {
      const seconds = (Date.parse(p.timestamp) - start) / 1000;
      return seconds >= quarter * 900 && seconds < (quarter + 1) * 900;
    }));
  }
});

test('dense bad-quality data and equal timestamps stay within the point budget', () => {
  const data = Array.from({ length: 3600 }, (_, i) => record(0, i % 7, 0));
  const sampled = sampleTrendPoints(data.reverse(), 50);
  assert.ok(sampled.length <= 50);
  assert.ok(sampled.every((p) => p.quality === 0));
  assert.equal(sampled[0], data.at(-1));
  assert.equal(sampled.at(-1), data[0]);
});
