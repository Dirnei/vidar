import React, { useCallback, useEffect, useState } from 'react';
import type { DiscoveredDevice, Room } from '../types';
import { getDiscoveredDevices, getRooms, configureDiscoveredDevice, discoverShellyDevice } from '../api/client';
import { ConfigureDeviceModal } from '../components/ConfigureDeviceModal';

export function DiscoveredPage() {
  const [discovered, setDiscovered] = useState<DiscoveredDevice[]>([]);
  const [rooms, setRooms] = useState<Room[]>([]);
  const [loading, setLoading] = useState(true);
  const [configuring, setConfiguring] = useState<DiscoveredDevice | null>(null);
  const [shellyHost, setShellyHost] = useState('');
  const [probing, setProbing] = useState(false);
  const [feedback, setFeedback] = useState<{ type: 'success' | 'error'; message: string } | null>(null);

  const loadData = useCallback(async () => {
    const [devs, roomList] = await Promise.all([getDiscoveredDevices(), getRooms()]);
    setDiscovered(devs);
    setRooms(roomList);
    setLoading(false);
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  async function handleConfigure(name: string, roomId: string) {
    if (!configuring) return;
    await configureDiscoveredDevice(configuring.id, { name, roomId });
    setConfiguring(null);
    await loadData();
  }

  async function handleDiscoverShelly() {
    const host = shellyHost.trim();
    if (!host) return;
    setProbing(true);
    setFeedback(null);
    try {
      const result = await discoverShellyDevice(host);
      if (result.status === 'discovered') {
        setFeedback({ type: 'success', message: `Device found at ${host}` });
        setShellyHost('');
      } else {
        setFeedback({ type: 'error', message: result.message ?? `No device found at ${host}` });
      }
      await loadData();
    } catch (err) {
      setFeedback({ type: 'error', message: `Failed to reach ${host}: ${err instanceof Error ? err.message : 'unknown error'}` });
    } finally {
      setProbing(false);
    }
  }

  const inputStyle: React.CSSProperties = {
    flex: 1,
    padding: '9px 13px',
    backgroundColor: 'var(--bg-hover)',
    border: '1px solid var(--border-default)',
    borderRadius: 'var(--radius-sm)',
    color: 'var(--text-primary)',
    fontSize: 14,
    outline: 'none',
    fontFamily: 'var(--font-body)',
    transition: 'border-color 0.2s, box-shadow 0.2s',
  };

  function handleInputFocus(e: React.FocusEvent<HTMLInputElement>) {
    e.target.style.borderColor = 'var(--accent-primary)';
    e.target.style.boxShadow = '0 0 0 3px var(--accent-primary-dim)';
  }

  function handleInputBlur(e: React.FocusEvent<HTMLInputElement>) {
    e.target.style.borderColor = 'var(--border-default)';
    e.target.style.boxShadow = 'none';
  }

  if (loading) {
    return <div style={{ color: 'var(--text-muted)', padding: 24, fontFamily: 'var(--font-body)' }}>Loading discovered devices…</div>;
  }

  return (
    <div className="page-content">
      <div className="page-title">Setup</div>

      {/* Add Shelly device card */}
      <div
        style={{
          background: 'var(--bg-elevated)',
          border: '1px solid var(--border-subtle)',
          borderRadius: 'var(--radius-lg)',
          padding: '20px 22px',
          marginBottom: 28,
          boxShadow: 'var(--shadow-card)',
        }}
      >
        <div
          style={{
            fontFamily: 'var(--font-heading)',
            fontSize: 14,
            fontWeight: 600,
            color: 'var(--text-secondary)',
            marginBottom: 14,
            letterSpacing: '-0.01em',
          }}
        >
          Add Shelly Device by IP
        </div>
        <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
          <input
            style={inputStyle}
            type="text"
            placeholder="Shelly IP address (e.g. 192.168.1.42)"
            value={shellyHost}
            disabled={probing}
            onChange={(e) => setShellyHost(e.target.value)}
            onFocus={handleInputFocus}
            onBlur={handleInputBlur}
            onKeyDown={(e) => { if (e.key === 'Enter') handleDiscoverShelly(); }}
          />
          <button
            className="btn-primary"
            style={{ flexShrink: 0, opacity: probing ? 0.5 : 1 }}
            disabled={probing}
            onClick={handleDiscoverShelly}
          >
            {probing ? 'Probing…' : 'Discover'}
          </button>
        </div>
        {feedback && (
          <div className={`feedback-banner ${feedback.type}`}>
            <span>{feedback.type === 'success' ? '✓' : '✕'}</span>
            {feedback.message}
          </div>
        )}
      </div>

      {/* Discovered devices list */}
      {discovered.length === 0 ? (
        <div style={{ color: 'var(--text-muted)', fontSize: 14 }}>
          No unconfigured devices found.
        </div>
      ) : (
        <div>
          {discovered.map((d) => (
            <div key={d.id} className="discovery-card">
              <div style={{ flex: 1, minWidth: 0 }}>
                {d.metadata?.name && (
                  <div
                    style={{
                      fontFamily: 'var(--font-heading)',
                      fontSize: 15,
                      fontWeight: 600,
                      color: 'var(--text-primary)',
                      marginBottom: 2,
                    }}
                  >
                    {d.metadata.name}
                  </div>
                )}
                <div
                  style={{
                    fontWeight: 600,
                    fontSize: 14,
                    color: d.metadata?.name ? 'var(--text-secondary)' : 'var(--text-primary)',
                    marginBottom: 4,
                  }}
                >
                  {d.nativeId}
                </div>
                <div style={{ fontSize: 12, color: 'var(--accent-teal)', marginBottom: 8 }}>
                  {d.communicationType}
                </div>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                  {d.capabilities.map((cap) => (
                    <span
                      key={cap}
                      style={{
                        display: 'inline-block',
                        padding: '2px 9px',
                        borderRadius: 4,
                        fontSize: 11,
                        fontWeight: 500,
                        backgroundColor: 'var(--bg-hover)',
                        color: 'var(--text-muted)',
                        border: '1px solid var(--border-subtle)',
                      }}
                    >
                      {cap}
                    </span>
                  ))}
                </div>
                {d.metadata && Object.keys(d.metadata).length > 0 && (
                  <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 6 }}>
                    {Object.entries(d.metadata)
                      .slice(0, 4)
                      .map(([k, v]) => `${k}: ${v}`)
                      .join(' · ')}
                  </div>
                )}
              </div>
              <button
                className="btn-primary"
                style={{ flexShrink: 0, alignSelf: 'center', fontSize: 13, padding: '7px 16px' }}
                onClick={() => setConfiguring(d)}
              >
                Configure
              </button>
            </div>
          ))}
        </div>
      )}

      {configuring && rooms.length > 0 && (
        <ConfigureDeviceModal
          rooms={rooms}
          defaultName={configuring.metadata?.name}
          onConfirm={handleConfigure}
          onCancel={() => setConfiguring(null)}
        />
      )}

      {configuring && rooms.length === 0 && (
        <div className="modal-overlay" onClick={() => setConfiguring(null)}>
          <div className="modal-dialog" style={{ maxWidth: 320, textAlign: 'center' }}>
            <div className="modal-title">No Rooms Available</div>
            <div style={{ fontSize: 13, color: 'var(--text-muted)', marginBottom: 4 }}>
              Please create a room first before configuring devices.
            </div>
            <div style={{ display: 'flex', justifyContent: 'center', marginTop: 8 }}>
              <button className="btn-secondary" onClick={() => setConfiguring(null)}>
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
