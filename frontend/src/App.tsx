import { useState } from 'react';
import { fetchHistory, fetchLatest } from './api';
import { TrendChart } from './TrendChart';
import { usePolling } from './usePolling';

const TAG_COUNT = 100;

const WINDOWS = [
  { label: '5 мин', ms: 5 * 60_000 },
  { label: '15 мин', ms: 15 * 60_000 },
  { label: '1 час', ms: 60 * 60_000 },
] as const;

const valueFormat = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 3, maximumFractionDigits: 3 });
const timeFormat = new Intl.DateTimeFormat('ru-RU', { hour: '2-digit', minute: '2-digit', second: '2-digit' });

export function App() {
  const [tagId, setTagId] = useState(1);
  const [windowMs, setWindowMs] = useState<number>(WINDOWS[0].ms);

  // Текущее значение — раз в секунду (как часто пишет эмулятор), тренд — раз в 5 секунд.
  const latest = usePolling((signal) => fetchLatest(tagId, signal), 1000, [tagId]);
  const history = usePolling((signal) => fetchHistory(tagId, windowMs, 1000, signal), 5000, [tagId, windowMs]);

  const current = latest.data;
  const isBad = current?.quality === 0;
  const ageSeconds = current ? Math.max(0, Math.round((Date.now() - Date.parse(current.timestamp)) / 1000)) : null;
  const isStale = ageSeconds !== null && ageSeconds > 5;
  const recent = (history.data ?? []).slice(0, 8);

  return (
    <div className="shell">
      <header className="topbar">
        <h1>Телеметрия SCADA</h1>
        <p className="topbar-meta">{TAG_COUNT} тегов, значение обновляется раз в секунду</p>
      </header>

      <main className="layout">
        <nav className="tags" aria-label="Выбор тега">
          <h2 className="panel-title">Теги</h2>
          <div className="tag-grid">
            {Array.from({ length: TAG_COUNT }, (_, i) => i + 1).map((id) => (
              <button
                key={id}
                type="button"
                className="tag"
                aria-pressed={id === tagId}
                onClick={() => setTagId(id)}
              >
                {id}
              </button>
            ))}
          </div>
        </nav>

        <section className="faceplate" aria-live="polite">
          <div className="readout">
            <p className="readout-tag">Тег {tagId}</p>
            <p className={`readout-value${isBad ? ' is-bad' : ''}`}>
              {current ? valueFormat.format(current.value) : '—'}
            </p>
            <dl className="readout-facts">
              <div>
                <dt>Достоверность</dt>
                <dd className={isBad ? 'fact-bad' : undefined}>
                  {current ? (isBad ? 'Недостоверно' : 'Достоверно') : 'Нет данных'}
                </dd>
              </div>
              <div>
                <dt>Время замера</dt>
                <dd>{current ? timeFormat.format(Date.parse(current.timestamp)) : '—'}</dd>
              </div>
              <div>
                <dt>Возраст</dt>
                <dd className={isStale ? 'fact-bad' : undefined}>{ageSeconds !== null ? `${ageSeconds} с` : '—'}</dd>
              </div>
            </dl>
            {latest.error && <p className="error">Нет связи с API: {latest.error}. Повтор через секунду.</p>}
            {!latest.error && latest.data === null && (
              <p className="hint">По этому тегу ещё нет замеров. Проверьте, что эмулятор включён (Emulator:Enabled).</p>
            )}
          </div>

          <div className="trend-panel">
            <div className="trend-head">
              <h2 className="panel-title">Тренд</h2>
              <div className="segmented" role="group" aria-label="Период тренда">
                {WINDOWS.map((w) => (
                  <button
                    key={w.ms}
                    type="button"
                    aria-pressed={w.ms === windowMs}
                    onClick={() => setWindowMs(w.ms)}
                  >
                    {w.label}
                  </button>
                ))}
              </div>
            </div>
            {history.data && <TrendChart points={history.data} windowMs={windowMs} />}
            {history.error && <p className="error">Не удалось загрузить историю: {history.error}</p>}
          </div>

          {recent.length > 0 && (
            <table className="recent">
              <caption className="panel-title">Последние замеры</caption>
              <thead>
                <tr>
                  <th scope="col">Время</th>
                  <th scope="col">Значение</th>
                  <th scope="col">Достоверность</th>
                </tr>
              </thead>
              <tbody>
                {recent.map((m) => (
                  <tr key={m.timestamp} className={m.quality === 0 ? 'row-bad' : undefined}>
                    <td>{timeFormat.format(Date.parse(m.timestamp))}</td>
                    <td className="num">{valueFormat.format(m.value)}</td>
                    <td>{m.quality === 0 ? 'Недостоверно' : 'Достоверно'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      </main>
    </div>
  );
}
