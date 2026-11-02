import { useEffect, useRef, useState } from 'react';

interface PollState<T> {
  data: T | undefined;
  error: string | null;
}

/**
 * Периодически вызывает loader. Следующий запрос уходит только после завершения предыдущего,
 * а при смене зависимостей текущий запрос отменяется (AbortController) — запросы не копятся.
 */
export function usePolling<T>(
  loader: (signal: AbortSignal) => Promise<T>,
  intervalMs: number,
  deps: readonly unknown[],
): PollState<T> {
  const [state, setState] = useState<PollState<T>>({ data: undefined, error: null });
  const loaderRef = useRef(loader);
  loaderRef.current = loader;

  useEffect(() => {
    const controller = new AbortController();
    let timer: number | undefined;

    const tick = async () => {
      try {
        const data = await loaderRef.current(controller.signal);
        setState({ data, error: null });
      } catch (error) {
        if (controller.signal.aborted) return;
        setState((prev) => ({ data: prev.data, error: (error as Error).message }));
      }
      if (!controller.signal.aborted) timer = window.setTimeout(tick, intervalMs);
    };

    setState({ data: undefined, error: null });
    void tick();

    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [intervalMs, ...deps]);

  return state;
}
