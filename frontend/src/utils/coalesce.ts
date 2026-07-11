import { useCallback, useEffect, useRef } from 'react';

/**
 * Coalesces bursts of "please reload" signals into at most one in-flight reload.
 *
 * The SSE stream can emit many `deviceStateChanged` events per second in a busy
 * home. Firing a full data reload per event saturates the CPU/network on low-end
 * devices (the original crash cause). This hook:
 *
 *  - debounces a burst of `schedule()` calls into a single trailing reload,
 *  - guarantees only ONE reload runs at a time (no overlapping fetches, so a
 *    slow older response can never overwrite a newer one — kills the stale-write
 *    race), and re-runs exactly once if signals arrived while it was busy,
 *  - cancels cleanly on unmount.
 *
 * `reload` may change identity between renders; the latest is always used.
 */
export function useCoalescedReload(reload: () => Promise<void>, delayMs = 350): () => void {
  const reloadRef = useRef(reload);
  reloadRef.current = reload;

  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const running = useRef(false);
  const queued = useRef(false);
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      if (timer.current) clearTimeout(timer.current);
    };
  }, []);

  const run = useCallback(async () => {
    if (running.current) {
      queued.current = true;
      return;
    }
    running.current = true;
    try {
      await reloadRef.current();
    } finally {
      running.current = false;
      if (queued.current && mounted.current) {
        queued.current = false;
        void run();
      }
    }
  }, []);

  return useCallback(() => {
    if (timer.current) return; // a reload is already scheduled for this window
    timer.current = setTimeout(() => {
      timer.current = null;
      if (mounted.current) void run();
    }, delayMs);
  }, [run, delayMs]);
}
