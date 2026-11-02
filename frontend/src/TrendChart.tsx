import { useEffect, useMemo, useRef, useState } from 'react';
import type { Measurement } from './api';

interface Props {
  points: Measurement[];
  windowMs: number;
}

const HEIGHT = 280;
const PAD = { top: 16, right: 8, bottom: 28, left: 64 };

const timeFormat = new Intl.DateTimeFormat('ru-RU', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
const valueFormat = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 });

/** Тренд значения за окно времени. Обычный SVG без библиотек; недостоверные точки отмечены янтарным. */
export function TrendChart({ points, windowMs }: Props) {
  // Рисуем в реальных пикселях контейнера, чтобы подписи не мельчали на узком экране.
  const containerRef = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(880);
  useEffect(() => {
    const element = containerRef.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) => setWidth(Math.max(280, Math.round(entry.contentRect.width))));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const chart = useMemo(() => {
    const WIDTH = width;
    const now = Date.now();
    const x0 = now - windowMs;
    const sorted = points
      .map((p) => ({ t: Date.parse(p.timestamp), v: p.value, bad: p.quality === 0 }))
      .sort((a, b) => a.t - b.t);

    if (sorted.length === 0) return null;

    let min = Math.min(...sorted.map((p) => p.v));
    let max = Math.max(...sorted.map((p) => p.v));
    if (min === max) {
      min -= 1;
      max += 1;
    }
    const margin = (max - min) * 0.1;
    min -= margin;
    max += margin;

    const innerW = WIDTH - PAD.left - PAD.right;
    const innerH = HEIGHT - PAD.top - PAD.bottom;
    const sx = (t: number) => PAD.left + ((t - x0) / windowMs) * innerW;
    const sy = (v: number) => PAD.top + (1 - (v - min) / (max - min)) * innerH;

    const path = sorted.map((p, i) => `${i === 0 ? 'M' : 'L'}${sx(p.t).toFixed(1)},${sy(p.v).toFixed(1)}`).join('');
    const yTicks = Array.from({ length: 5 }, (_, i) => min + ((max - min) * i) / 4);
    const xCount = WIDTH < 500 ? 3 : 5;
    const xTicks = Array.from({ length: xCount }, (_, i) => x0 + (windowMs * i) / (xCount - 1));
    const bad = sorted.filter((p) => p.bad).map((p) => ({ x: sx(p.t), y: sy(p.v), t: p.t }));

    return { path, yTicks, xTicks, bad, sx, sy, WIDTH };
  }, [points, windowMs, width]);

  return (
    <div ref={containerRef}>
      {chart ? (
        <svg className="trend" viewBox={`0 0 ${chart.WIDTH} ${HEIGHT}`} role="img" aria-label="Тренд значения тега">
          {chart.yTicks.map((v) => (
            <g key={v}>
              <line className="trend-grid" x1={PAD.left} x2={chart.WIDTH - PAD.right} y1={chart.sy(v)} y2={chart.sy(v)} />
              <text className="trend-label" x={PAD.left - 8} y={chart.sy(v)} textAnchor="end" dominantBaseline="middle">
                {valueFormat.format(v)}
              </text>
            </g>
          ))}
          {chart.xTicks.map((t, i) => (
            <text
              key={t}
              className="trend-label"
              x={chart.sx(t)}
              y={HEIGHT - 8}
              textAnchor={i === 0 ? 'start' : i === chart.xTicks.length - 1 ? 'end' : 'middle'}
            >
              {timeFormat.format(t)}
            </text>
          ))}
          <path className="trend-line" d={chart.path} />
          {chart.bad.map((p) => (
            <rect key={p.t} className="trend-bad" x={p.x - 4} y={p.y - 4} width={8} height={8}>
              <title>Недостоверный замер, {timeFormat.format(p.t)}</title>
            </rect>
          ))}
        </svg>
      ) : (
        <p className="trend-empty">За выбранный период замеров нет. Эмулятор пишет новые точки раз в секунду.</p>
      )}
    </div>
  );
}
