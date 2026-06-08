import React, { useCallback, useEffect, useState } from 'react';
import type { DiscoveredDevice, Room } from '../types';
import { getDiscoveredDevices, getRooms, configureDiscoveredDevice, discoverShellyDevice } from '../api/client';
import { ConfigureDeviceModal } from '../components/ConfigureDeviceModal';
import { CapabilityIcon, primaryCapabilityIcon } from '../components/CapabilityIcon';

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
    minWidth: 0,
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
        <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
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
              {/* Large icon */}
              <div style={{
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                width: 52, height: 52, borderRadius: 'var(--radius-sm)', flexShrink: 0,
                background: 'var(--bg-hover)', border: '1px solid var(--border-subtle)',
              }}>
                <CapabilityIcon capability={primaryCapabilityIcon(d.capabilities)} size={28} />
              </div>
              {/* Info */}
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{
                  fontFamily: 'var(--font-heading)', fontSize: 15, fontWeight: 600,
                  color: 'var(--text-primary)', marginBottom: 2,
                }}>
                  {d.metadata?.name ?? d.metadata?.friendly_name ?? d.nativeId}
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 6 }}>
                  {(d.metadata?.name || d.metadata?.friendly_name) && (
                    <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>{d.nativeId}</span>
                  )}
                  <span style={{
                    fontSize: 10, fontWeight: 600, textTransform: 'uppercase' as const,
                    letterSpacing: '0.05em', padding: '1px 7px', borderRadius: 3,
                    background: d.communicationType === 'shelly'
                      ? 'color-mix(in srgb, var(--accent-primary) 15%, transparent)'
                      : 'color-mix(in srgb, var(--accent-teal) 15%, transparent)',
                    color: d.communicationType === 'shelly' ? 'var(--accent-primary)' : 'var(--accent-teal)',
                  }}>
                    {d.communicationType}
                  </span>
                </div>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                  {d.capabilities.map((cap) => (
                    <span
                      key={cap}
                      style={{
                        display: 'inline-flex', alignItems: 'center', gap: 4,
                        padding: '2px 9px', borderRadius: 4, fontSize: 11, fontWeight: 500,
                        backgroundColor: 'var(--bg-hover)', color: 'var(--text-secondary)',
                        border: '1px solid var(--border-subtle)',
                      }}
                    >
                      <CapabilityIcon capability={cap} size={11} />
                      {cap}
                    </span>
                  ))}
                </div>
                {d.metadata?.vendor && (
                  <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 6 }}>
                    {[d.metadata.vendor, d.metadata.model].filter(Boolean).join(' · ')}
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
          defaultName={configuring.metadata?.name ?? configuring.metadata?.friendly_name}
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
