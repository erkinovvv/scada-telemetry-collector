export type Quality = 0 | 1;

export interface Measurement {
  tagId: number;
  timestamp: string;
  value: number;
  quality: Quality;
}

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T | null> {
  const response = await fetch(url, { signal });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`Сервер ответил ${response.status} на ${url}`);
  return (await response.json()) as T;
}

export function fetchLatest(tagId: number, signal?: AbortSignal) {
  return getJson<Measurement>(`/api/tags/${tagId}/latest`, signal);
}

export async function fetchHistory(tagId: number, windowMs: number, limit: number, signal?: AbortSignal) {
  const to = new Date();
  const from = new Date(to.getTime() - windowMs);
  const query = new URLSearchParams({ from: from.toISOString(), to: to.toISOString(), limit: String(limit) });
  return (await getJson<Measurement[]>(`/api/tags/${tagId}/history?${query}`, signal)) ?? [];
}
