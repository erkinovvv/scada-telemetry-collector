import type { Measurement } from './api';

const EMULATOR_INTERVAL_MS = 1000;
const MAX_HISTORY_LIMIT = 10_000;

/** Полное окно эмулятора, включая запас на точку у границы периода. */
export function getHistoryLimit(windowMs: number): number {
  return Math.min(MAX_HISTORY_LIMIT, Math.ceil(windowMs / EMULATOR_INTERVAL_MS) + 1);
}

/**
 * API отдаёт новые точки первыми. Для графика возвращаем хронологический порядок;
 * в каждом временном сегменте сохраняем минимум, максимум и недостоверную точку.
 * Края периода сохраняются всегда, исходный массив не изменяется.
 */
export function sampleTrendPoints(points: readonly Measurement[], maxPoints = 1000): Measurement[] {
  if (!Number.isInteger(maxPoints) || maxPoints < 5) {
    throw new RangeError('maxPoints must be an integer of at least 5.');
  }

  const ordered = [...points].reverse();
  if (ordered.length <= maxPoints) return ordered;

  const last = ordered.length - 1;
  const start = Date.parse(ordered[0].timestamp);
  const duration = Date.parse(ordered[last].timestamp) - start;
  const bucketCount = Math.floor((maxPoints - 2) / 3);
  const buckets = new Array<{ min: number; max: number; bad?: number } | undefined>(bucketCount);

  for (let i = 1; i < last; i++) {
    const fraction = duration > 0 ? (Date.parse(ordered[i].timestamp) - start) / duration : i / last;
    const bucketIndex = Math.min(bucketCount - 1, Math.floor(fraction * bucketCount));
    const bucket = buckets[bucketIndex] ??= { min: i, max: i };
    if (ordered[i].value < ordered[bucket.min].value) bucket.min = i;
    if (ordered[i].value > ordered[bucket.max].value) bucket.max = i;
    if (ordered[i].quality === 0) bucket.bad ??= i;
  }

  const result = [ordered[0]];
  for (const bucket of buckets) {
    if (!bucket) continue;
    const indices = bucket.bad === undefined ? [bucket.min, bucket.max] : [bucket.min, bucket.max, bucket.bad];
    indices.sort((a, b) => a - b);
    for (let i = 0; i < indices.length; i++) {
      if (i === 0 || indices[i] !== indices[i - 1]) result.push(ordered[indices[i]]);
    }
  }
  result.push(ordered[last]);
  return result;
}
