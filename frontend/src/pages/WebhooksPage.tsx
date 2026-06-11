import React, { useEffect, useState, useRef, useCallback } from 'react';
import { getWebhookRoutes, getWebhookEvents, getWebhookPayload } from '../api/client';
import type { WebhookRoute, WebhookEvent, WebhookEventPage, WebhookHandledEvent } from '../types';

// ---- Helpers ----

function relativeTime(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  if (diff < 0) return 'just now';
  const seconds = Math.floor(diff / 1000);
  if (seconds < 10) return 'just now';
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

function humanSize(bytes: number): string {
  if (bytes < 1024) return '< 1 KB';
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function formatLatency(receivedAt: string, handledAt: string | null): string {
  if (!handledAt) return 'pending...';
  const ms = new Date(handledAt).getTime() - new Date(receivedAt).getTime();
  if (ms < 1000) return `${ms}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

function statusColor(status: string): string {
  switch (status) {
    case 'handled': return 'var(--accent-green)';
    case 'failed': return 'var(--accent-red)';
    default: return 'var(--accent-primary)';
  }
}

const PAGE_SIZE = 20;
const PAYLOAD_DISPLAY_LIMIT = 4096;

// ---- Styles ----

const pageStyle: React.CSSProperties = {
  maxWidth: 900,
};

const headingStyle: React.CSSProperties = {
  fontFamily: 'var(--font-heading)',
  fontSize: 26,
  fontWeight: 700,
  color: 'var(--text-primary)',
  letterSpacing: '-0.02em',
  marginBottom: 6,
};

const subtitleStyle: React.CSSProperties = {
  fontSize: 13,
  color: 'var(--text-muted)',
  marginBottom: 32,
};

const sectionLabelStyle: React.CSSProperties = {
  fontSize: 11,
  fontWeight: 600,
  color: 'var(--text-muted)',
  textTransform: 'uppercase',
  letterSpacing: '0.08em',
  marginBottom: 12,
  marginTop: 28,
};

const cardStyle: React.CSSProperties = {
  background: 'var(--bg-elevated)',
  border: '1px solid var(--border-default)',
  borderRadius: 'var(--radius-md)',
  padding: '22px 24px',
  boxShadow: 'var(--shadow-card)',
  marginBottom: 16,
};

const errorBannerStyle: React.CSSProperties = {
  background: 'color-mix(in srgb, var(--accent-red) 12%, transparent)',
  border: '1px solid color-mix(in srgb, var(--accent-red) 35%, transparent)',
  borderRadius: 'var(--radius-sm)',
  padding: '10px 14px',
  marginBottom: 20,
  fontSize: 13,
  color: 'var(--accent-red)',
};

// ---- WebhookUrlRow (duplicated from ApplicationsPage per spec) ----

function WebhookUrlRow({ route }: { route: WebhookRoute }) {
  const [copied, setCopied] = useState(false);
  const url = `${window.location.origin}${route.path}`;
  const canCopy = typeof navigator !== 'undefined' && !!navigator.clipboard;

  async function copy() {
    try {
      await navigator.clipboard.writeText(url);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // clipboard unavailable
    }
  }

  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <code
          style={{
            background: 'var(--bg-surface)',
            border: '1px solid var(--border-default)',
            borderRadius: 'var(--radius-sm)',
            padding: '6px 10px',
            fontSize: 12,
            color: 'var(--text-primary)',
            overflowX: 'auto',
            whiteSpace: 'nowrap',
            flex: 1,
            userSelect: 'all',
          }}
        >
          {url}
        </code>
        {canCopy && (
          <button
            type="button"
            onClick={copy}
            style={{
              background: 'none',
              border: '1px solid var(--border-default)',
              borderRadius: 'var(--radius-sm)',
              padding: '5px 10px',
              cursor: 'pointer',
              fontSize: 12,
              color: copied ? 'var(--accent-primary)' : 'var(--text-muted)',
              fontFamily: 'var(--font-body)',
              flexShrink: 0,
            }}
          >
            {copied ? 'copied' : 'copy'}
          </button>
        )}
      </div>
      {route.authMode === 'HeaderToken' && route.headerName && (
        <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 3 }}>
          requires token in header {route.headerName}
        </div>
      )}
    </div>
  );
}

// ---- WebhookUrlSection ----

function WebhookUrlSection({ routes }: { routes: WebhookRoute[] }) {
  if (routes.length === 0) return null;

  return (
    <>
      <div style={sectionLabelStyle}>Webhook URLs</div>
      <div style={cardStyle}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          {routes.map(r => (
            <WebhookUrlRow key={r.routeKey} route={r} />
          ))}
        </div>
      </div>
    </>
  );
}

// ---- PayloadPreview ----

function PayloadPreview({
  payloadId,
  contentType,
  payloadCache,
  onLoaded,
}: {
  payloadId: string;
  contentType: string;
  payloadCache: React.MutableRefObject<Record<string, string>>;
  onLoaded: () => void;
}) {
  const [text, setText] = useState<string | null>(payloadCache.current[payloadId] ?? null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (text !== null) return;
    let cancelled = false;
    getWebhookPayload(payloadId)
      .then(raw => {
        if (cancelled) return;
        payloadCache.current[payloadId] = raw;
        setText(raw);
        onLoaded();
      })
      .catch(e => {
        if (cancelled) return;
        setError(e instanceof Error ? e.message : 'Failed to load payload');
      });
    return () => { cancelled = true; };
  }, [payloadId, text, payloadCache, onLoaded]);

  if (error) {
    return <div style={{ fontSize: 12, color: 'var(--accent-red)', padding: '8px 0' }}>{error}</div>;
  }

  if (text === null) {
    return <div style={{ fontSize: 12, color: 'var(--text-muted)', padding: '8px 0' }}>Loading payload...</div>;
  }

  let display = text;
  if (contentType.includes('json')) {
    try {
      display = JSON.stringify(JSON.parse(text), null, 2);
    } catch {
      // show raw text
    }
  }

  const truncated = display.length > PAYLOAD_DISPLAY_LIMIT;
  if (truncated) {
    display = display.slice(0, PAYLOAD_DISPLAY_LIMIT);
  }

  return (
    <div style={{ padding: '8px 0' }}>
      <pre
        style={{
          background: 'var(--bg-surface)',
          border: '1px solid var(--border-subtle)',
          borderRadius: 'var(--radius-sm)',
          padding: '12px 14px',
          fontSize: 12,
          fontFamily: 'monospace',
          color: 'var(--text-primary)',
          overflowX: 'auto',
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-all',
          margin: 0,
          maxHeight: 400,
          overflowY: 'auto',
        }}
      >
        {display}
      </pre>
      {truncated && (
        <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 4 }}>
          Payload truncated to ~4 KB for display. Full size: {humanSize(text.length)}
        </div>
      )}
    </div>
  );
}

// ---- EventHistoryTable ----

function EventHistoryTable({
  routes,
  routeFilter,
  onRouteFilterChange,
}: {
  routes: WebhookRoute[];
  routeFilter: string;
  onRouteFilterChange: (v: string) => void;
}) {
  const [page, setPage] = useState<WebhookEventPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [skip, setSkip] = useState(0);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [newEventCount, setNewEventCount] = useState(0);
  const payloadCache = useRef<Record<string, string>>({});
  const eventSourceRef = useRef<EventSource | null>(null);

  const activeRouteKey = routeFilter || undefined;

  // Fetch events
  const fetchEvents = useCallback(async (currentSkip: number) => {
    setLoading(true);
    setError(null);
    try {
      const data = await getWebhookEvents(activeRouteKey, currentSkip, PAGE_SIZE);
      setPage(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load events');
    } finally {
      setLoading(false);
    }
  }, [activeRouteKey]);

  // On mount + filter/page change
  useEffect(() => {
    fetchEvents(skip);
  }, [fetchEvents, skip]);

  // Reset skip when filter changes
  useEffect(() => {
    setSkip(0);
    setNewEventCount(0);
    setExpandedId(null);
  }, [routeFilter]);

  // SSE
  useEffect(() => {
    const params = new URLSearchParams();
    if (activeRouteKey) params.set('routeKey', activeRouteKey);
    const url = `/api/webhooks/events/stream${params.toString() ? '?' + params.toString() : ''}`;
    const es = new EventSource(url);
    eventSourceRef.current = es;

    es.onmessage = (msg) => {
      try {
        const event: WebhookEvent = JSON.parse(msg.data);
        setSkip(currentSkip => {
          if (currentSkip === 0) {
            // On page 1: prepend
            setPage(prev => {
              if (!prev) return prev;
              // Avoid duplicates
              if (prev.items.some(i => i.payloadId === event.payloadId)) return prev;
              const items = [event, ...prev.items].slice(0, PAGE_SIZE);
              return { items, totalCount: prev.totalCount + 1 };
            });
          } else {
            // On later pages: accumulate count
            setNewEventCount(c => c + 1);
          }
          return currentSkip;
        });
      } catch {
        // ignore malformed SSE
      }
    };

    es.addEventListener('webhook-handled', (msg) => {
      try {
        const handled: WebhookHandledEvent = JSON.parse((msg as MessageEvent).data);
        setPage(prev => {
          if (!prev) return prev;
          const items = prev.items.map(item =>
            item.payloadId === handled.payloadId
              ? { ...item, status: handled.status as WebhookEvent['status'], handledAt: handled.handledAt, error: handled.error }
              : item
          );
          return { ...prev, items };
        });
      } catch {
        // ignore malformed SSE
      }
    });

    es.onerror = () => {
      // EventSource will auto-reconnect
    };

    return () => {
      es.close();
      eventSourceRef.current = null;
    };
  }, [activeRouteKey]);

  function goToPage1() {
    setSkip(0);
    setNewEventCount(0);
  }

  const totalCount = page?.totalCount ?? 0;
  const items = page?.items ?? [];
  const showingFrom = totalCount === 0 ? 0 : skip + 1;
  const showingTo = Math.min(skip + PAGE_SIZE, totalCount);
  const hasPrev = skip > 0;
  const hasNext = skip + PAGE_SIZE < totalCount;

  const noopCallback = useCallback(() => {}, []);

  return (
    <>
      <div style={sectionLabelStyle}>Event History</div>

      {/* Filter + new events banner */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 12 }}>
        <select
          value={routeFilter}
          onChange={e => onRouteFilterChange(e.target.value)}
          style={{
            background: 'var(--bg-elevated)',
            border: '1px solid var(--border-default)',
            borderRadius: 'var(--radius-sm)',
            padding: '6px 10px',
            fontSize: 13,
            color: 'var(--text-primary)',
            fontFamily: 'var(--font-body)',
            cursor: 'pointer',
            outline: 'none',
          }}
        >
          <option value="">All routes</option>
          {routes.map(r => (
            <option key={r.routeKey} value={r.routeKey}>{r.routeKey}</option>
          ))}
        </select>

        {newEventCount > 0 && (
          <button
            type="button"
            onClick={goToPage1}
            style={{
              background: 'var(--accent-primary-dim)',
              border: '1px solid var(--accent-primary)',
              borderRadius: 'var(--radius-sm)',
              padding: '4px 12px',
              fontSize: 12,
              color: 'var(--accent-primary)',
              fontFamily: 'var(--font-body)',
              cursor: 'pointer',
              fontWeight: 600,
            }}
          >
            {newEventCount} new event{newEventCount !== 1 ? 's' : ''}
          </button>
        )}
      </div>

      {error && <div style={errorBannerStyle}>{error}</div>}

      {loading && !page && (
        <div style={{ color: 'var(--text-muted)', fontFamily: 'var(--font-body)', padding: '24px 0', textAlign: 'center' }}>
          Loading...
        </div>
      )}

      {!loading && items.length === 0 && !error && (
        <div style={{ textAlign: 'center', padding: '48px 0' }}>
          <div style={{ fontSize: 14, color: 'var(--text-secondary)', marginBottom: 6 }}>
            No webhook events received yet
          </div>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
            Configure webhook URLs above to start receiving events
          </div>
        </div>
      )}

      {items.length > 0 && (
        <div style={{
          background: 'var(--bg-elevated)',
          border: '1px solid var(--border-default)',
          borderRadius: 'var(--radius-md)',
          boxShadow: 'var(--shadow-card)',
          overflow: 'hidden',
        }}>
          {/* Table header */}
          <div style={{
            display: 'grid',
            gridTemplateColumns: '1fr 140px 80px 100px 80px',
            gap: 0,
            padding: '10px 16px',
            borderBottom: '1px solid var(--border-subtle)',
            fontSize: 11,
            fontWeight: 600,
            color: 'var(--text-muted)',
            textTransform: 'uppercase',
            letterSpacing: '0.06em',
          }}>
            <div>Received</div>
            <div>Route</div>
            <div>Status</div>
            <div>Latency</div>
            <div style={{ textAlign: 'right' }}>Size</div>
          </div>

          {/* Table rows */}
          {items.map(event => {
            const isExpanded = expandedId === event.payloadId;
            return (
              <div key={event.payloadId}>
                <div
                  onClick={() => setExpandedId(isExpanded ? null : event.payloadId)}
                  style={{
                    display: 'grid',
                    gridTemplateColumns: '1fr 140px 80px 100px 80px',
                    gap: 0,
                    padding: '10px 16px',
                    borderBottom: '1px solid var(--border-subtle)',
                    fontSize: 13,
                    color: 'var(--text-primary)',
                    cursor: 'pointer',
                    transition: 'background 0.1s',
                    background: isExpanded ? 'var(--bg-hover)' : 'transparent',
                  }}
                  onMouseEnter={e => {
                    if (!isExpanded) e.currentTarget.style.background = 'var(--bg-hover)';
                  }}
                  onMouseLeave={e => {
                    if (!isExpanded) e.currentTarget.style.background = 'transparent';
                  }}
                >
                  <div title={event.receivedAt}>{relativeTime(event.receivedAt)}</div>
                  <div>
                    <span style={{
                      fontSize: 11,
                      fontWeight: 600,
                      padding: '2px 7px',
                      borderRadius: 'var(--radius-sm)',
                      background: 'var(--bg-hover)',
                      color: 'var(--text-secondary)',
                    }}>
                      {event.routeKey}
                    </span>
                  </div>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12 }}>
                    <span style={{
                      width: 8, height: 8, borderRadius: '50%',
                      background: statusColor(event.status),
                      display: 'inline-block',
                      opacity: event.status === 'pending' ? 0.6 : 1,
                    }} />
                    <span style={{ color: 'var(--text-secondary)' }}>{event.status}</span>
                  </div>
                  <div style={{ color: 'var(--text-secondary)', fontSize: 12 }}>
                    {formatLatency(event.receivedAt, event.handledAt)}
                  </div>
                  <div style={{ textAlign: 'right', color: 'var(--text-secondary)', fontSize: 12 }}>
                    {humanSize(event.contentLength)}
                  </div>
                </div>
                {isExpanded && (
                  <div style={{
                    padding: '4px 16px 12px',
                    borderBottom: '1px solid var(--border-subtle)',
                    background: 'var(--bg-hover)',
                  }}>
                    {event.status === 'failed' && event.error && (
                      <div style={{
                        background: 'color-mix(in srgb, var(--accent-red) 12%, transparent)',
                        border: '1px solid color-mix(in srgb, var(--accent-red) 35%, transparent)',
                        borderRadius: 'var(--radius-sm)',
                        padding: '8px 12px',
                        marginBottom: 8,
                        fontSize: 12,
                        color: 'var(--accent-red)',
                      }}>
                        {event.error}
                      </div>
                    )}
                    <PayloadPreview
                      payloadId={event.payloadId}
                      contentType={event.contentType}
                      payloadCache={payloadCache}
                      onLoaded={noopCallback}
                    />
                  </div>
                )}
              </div>
            );
          })}

          {/* Pagination */}
          <div style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '10px 16px',
            fontSize: 12,
            color: 'var(--text-muted)',
          }}>
            <span>Showing {showingFrom}-{showingTo} of {totalCount}</span>
            <div style={{ display: 'flex', gap: 8 }}>
              <button
                type="button"
                disabled={!hasPrev}
                onClick={() => setSkip(s => Math.max(0, s - PAGE_SIZE))}
                style={{
                  background: 'none',
                  border: '1px solid var(--border-default)',
                  borderRadius: 'var(--radius-sm)',
                  padding: '4px 12px',
                  fontSize: 12,
                  color: hasPrev ? 'var(--text-secondary)' : 'var(--text-muted)',
                  fontFamily: 'var(--font-body)',
                  cursor: hasPrev ? 'pointer' : 'default',
                  opacity: hasPrev ? 1 : 0.5,
                }}
              >
                Previous
              </button>
              <button
                type="button"
                disabled={!hasNext}
                onClick={() => setSkip(s => s + PAGE_SIZE)}
                style={{
                  background: 'none',
                  border: '1px solid var(--border-default)',
                  borderRadius: 'var(--radius-sm)',
                  padding: '4px 12px',
                  fontSize: 12,
                  color: hasNext ? 'var(--text-secondary)' : 'var(--text-muted)',
                  fontFamily: 'var(--font-body)',
                  cursor: hasNext ? 'pointer' : 'default',
                  opacity: hasNext ? 1 : 0.5,
                }}
              >
                Next
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

// ---- WebhooksPage ----

export function WebhooksPage() {
  const [routes, setRoutes] = useState<WebhookRoute[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [routeFilter, setRouteFilter] = useState('');

  useEffect(() => {
    getWebhookRoutes()
      .then(setRoutes)
      .catch(e => setError(e instanceof Error ? e.message : 'Failed to load webhook routes'))
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return (
      <div className="main-inner">
        <div className="page-content" style={{ color: 'var(--text-muted)', fontFamily: 'var(--font-body)' }}>
          Loading...
        </div>
      </div>
    );
  }

  return (
    <div className="main-inner">
      <div className="page-content" style={pageStyle}>
        <div style={headingStyle}>Webhooks</div>
        <div style={subtitleStyle}>Incoming webhook URLs and event history.</div>

        {error && <div style={errorBannerStyle}>{error}</div>}

        <WebhookUrlSection routes={routes} />

        <EventHistoryTable
          routes={routes}
          routeFilter={routeFilter}
          onRouteFilterChange={setRouteFilter}
        />
      </div>
    </div>
  );
}
