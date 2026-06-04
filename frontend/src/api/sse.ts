export type DeviceStateChangedEvent = {
  deviceId: string;
  state: Record<string, unknown>;
};

type Handler = (evt: DeviceStateChangedEvent) => void;

let es: EventSource | null = null;
const handlers = new Set<Handler>();

export function subscribeDeviceState(handler: Handler): () => void {
  handlers.add(handler);
  ensureConnected();
  return () => {
    handlers.delete(handler);
    if (handlers.size === 0) disconnect();
  };
}

function ensureConnected() {
  if (es) return;
  es = new EventSource('/api/sse/state');

  es.addEventListener('deviceStateChanged', (raw) => {
    try {
      const data: DeviceStateChangedEvent = JSON.parse((raw as MessageEvent).data);
      handlers.forEach((h) => h(data));
    } catch {
      // ignore parse errors
    }
  });

  es.onerror = () => {
    // reconnect on error — browser EventSource auto-reconnects
  };
}

function disconnect() {
  es?.close();
  es = null;
}
