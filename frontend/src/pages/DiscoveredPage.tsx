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
    try {
      await discoverShellyDevice(host);
    } catch {
      // ignore — 202 may appear as non-JSON which throws in our client
    }
    setTimeout(async () => {
      await loadData();
      setProbing(false);
      setShellyHost('');
    }, 3000);
  }

  const pageTitle: React.CSSProperties = {
    fontSize: 20,
    fontWeight: 600,
    color: 'var(--text-primary)',
    marginBottom: 16,
  };

  const card: React.CSSProperties = {
    backgroundColor: 'var(--bg-card)',
    border: '1px solid var(--border)',
    borderRadius: 8,
    padding: '14px 16px',
    marginBottom: 8,
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: 12,
  };

  const cardMain: React.CSSProperties = {
    flex: 1,
    minWidth: 0,
  };

  const nativeId: React.CSSProperties = {
    fontWeight: 600,
    fontSize: 14,
    color: 'var(--text-primary)',
    marginBottom: 4,
  };

  const pill: React.CSSProperties = {
    display: 'inline-block',
    padding: '2px 8px',
    borderRadius: 4,
    fontSize: 11,
    fontWeight: 500,
    backgroundColor: 'var(--bg-row)',
    color: 'var(--text-muted)',
    border: '1px solid var(--border)',
    marginRight: 4,
    marginBottom: 4,
  };

  const commType: React.CSSProperties = {
    fontSize: 12,
    color: 'var(--accent-blue)',
    marginBottom: 6,
  };

  const configBtn: React.CSSProperties = {
    padding: '6px 14px',
    backgroundColor: 'var(--tab-active)',
    color: '#fff',
    borderRadius: 6,
    fontSize: 13,
    fontWeight: 500,
    border: 'none',
    cursor: 'pointer',
    flexShrink: 0,
    alignSelf: 'center',
  };

  const metaLine: React.CSSProperties = {
    fontSize: 12,
    color: 'var(--text-dimmed)',
    marginTop: 4,
  };

  const addCard: React.CSSProperties = {
    backgroundColor: 'var(--bg-card)',
    border: '1px solid var(--border)',
    borderRadius: 8,
    padding: '14px 16px',
    marginBottom: 20,
  };

  const addCardTitle: React.CSSProperties = {
    fontSize: 13,
    fontWeight: 600,
    color: 'var(--text-secondary)',
    marginBottom: 10,
  };

  const addRow: React.CSSProperties = {
    display: 'flex',
    gap: 8,
    alignItems: 'center',
  };

  const addInput: React.CSSProperties = {
    flex: 1,
    padding: '7px 10px',
    backgroundColor: 'var(--bg-row)',
    border: '1px solid var(--border)',
    borderRadius: 6,
    color: 'var(--text-primary)',
    fontSize: 13,
    outline: 'none',
  };

  const addBtn: React.CSSProperties = {
    padding: '7px 16px',
    backgroundColor: probing ? 'var(--bg-row)' : 'var(--tab-active)',
    color: probing ? 'var(--text-muted)' : '#fff',
    borderRadius: 6,
    fontSize: 13,
    fontWeight: 500,
    border: '1px solid var(--border)',
    cursor: probing ? 'default' : 'pointer',
    flexShrink: 0,
    transition: 'background-color 0.15s',
  };

  if (loading) {
    return <div style={{ color: 'var(--text-muted)', padding: 24 }}>Loading discovered devices…</div>;
  }

  return (
    <div>
      <div style={pageTitle}>Discovered Devices</div>

      <div style={addCard}>
        <div style={addCardTitle}>Add Shelly Device by IP</div>
        <div style={addRow}>
          <input
            style={addInput}
            type="text"
            placeholder="Shelly IP address (e.g. 192.168.1.42)"
            value={shellyHost}
            disabled={probing}
            onChange={(e) => setShellyHost(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') handleDiscoverShelly(); }}
          />
          <button style={addBtn} disabled={probing} onClick={handleDiscoverShelly}>
            {probing ? 'Probing…' : 'Discover'}
          </button>
        </div>
      </div>

      {discovered.length === 0 ? (
        <div style={{ color: 'var(--text-dimmed)', fontSize: 14 }}>
          No unconfigured devices found.
        </div>
      ) : (
        <div>
          {discovered.map((d) => (
            <div key={d.id} style={card}>
              <div style={cardMain}>
                <div style={nativeId}>{d.nativeId}</div>
                <div style={commType}>{d.communicationType}</div>
                <div>
                  {d.capabilities.map((cap) => (
                    <span key={cap} style={pill}>{cap}</span>
                  ))}
                </div>
                {d.metadata && Object.keys(d.metadata).length > 0 && (
                  <div style={metaLine}>
                    {Object.entries(d.metadata)
                      .slice(0, 4)
                      .map(([k, v]) => `${k}: ${v}`)
                      .join(' · ')}
                  </div>
                )}
              </div>
              <button style={configBtn} onClick={() => setConfiguring(d)}>
                Configure
              </button>
            </div>
          ))}
        </div>
      )}

      {configuring && rooms.length > 0 && (
        <ConfigureDeviceModal
          rooms={rooms}
          onConfirm={handleConfigure}
          onCancel={() => setConfiguring(null)}
        />
      )}

      {configuring && rooms.length === 0 && (
        <div style={{
          position: 'fixed',
          inset: 0,
          backgroundColor: 'rgba(0,0,0,0.6)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          zIndex: 1000,
        }}>
          <div style={{
            backgroundColor: 'var(--bg-card)',
            border: '1px solid var(--border)',
            borderRadius: 10,
            padding: 24,
            color: 'var(--text-secondary)',
            textAlign: 'center',
            maxWidth: 320,
          }}>
            <div style={{ fontSize: 15, fontWeight: 600, marginBottom: 8 }}>No Rooms Available</div>
            <div style={{ fontSize: 13, color: 'var(--text-muted)', marginBottom: 16 }}>
              Please create a room first before configuring devices.
            </div>
            <button
              style={{ padding: '8px 18px', backgroundColor: 'var(--bg-row)', border: '1px solid var(--border)', borderRadius: 6, color: 'var(--text-secondary)', cursor: 'pointer' }}
              onClick={() => setConfiguring(null)}
            >
              Close
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
