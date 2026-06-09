import React, { useCallback, useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import type { Device, Room, StateHistoryEntry, CommandHistoryEntry } from '../types';
import { getDevice, getRooms, sendCommand, updateDeviceSettings, deleteDevice, getDeviceStateHistory, getDeviceCommandHistory } from '../api/client';
import { subscribeDeviceState } from '../api/sse';
import { ToggleSwitch } from '../components/ToggleSwitch';
import { ProgressBar } from '../components/ProgressBar';
import { StatusDot } from '../components/StatusDot';
import { SliderControl } from '../components/SliderControl';
import { CapabilityIcon, primaryCapabilityIcon } from '../components/CapabilityIcon';
import { ColorWheel, ColorTempSlider } from '../components/ColorPicker';
import { useExpertMode } from '../components/ExpertMode';

export function DeviceDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { expert } = useExpertMode();
  const [device, setDevice] = useState<Device | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // History state
  const [historyTab, setHistoryTab] = useState<'state' | 'commands'>('state');
  const [stateHistory, setStateHistory] = useState<StateHistoryEntry[]>([]);
  const [commandHistory, setCommandHistory] = useState<CommandHistoryEntry[]>([]);
  const [historySkip, setHistorySkip] = useState(0);
  const [historyLoading, setHistoryLoading] = useState(false);

  // Edit mode state
  const [editing, setEditing] = useState(false);
  const [editName, setEditName] = useState('');
  const [editRoomId, setEditRoomId] = useState('');
  const [editHost, setEditHost] = useState('');
  const [rooms, setRooms] = useState<Room[]>([]);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const loadDevice = useCallback(async () => {
    if (!id) return;
    try {
      const d = await getDevice(id);
      setDevice(d);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load device');
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    loadDevice();
    const unsub = subscribeDeviceState((evt) => {
      if (evt.deviceId === id) loadDevice();
    });
    return unsub;
  }, [loadDevice, id]);

  useEffect(() => {
    if (!id || !expert) return;
    setHistorySkip(0);
    setStateHistory([]);
    setCommandHistory([]);
  }, [id, expert, historyTab]);

  const loadHistory = useCallback(async (skip: number) => {
    if (!id) return;
    setHistoryLoading(true);
    try {
      if (historyTab === 'state') {
        const entries = await getDeviceStateHistory(id, skip, 20);
        setStateHistory(prev => skip === 0 ? entries : [...prev, ...entries]);
      } else {
        const entries = await getDeviceCommandHistory(id, skip, 20);
        setCommandHistory(prev => skip === 0 ? entries : [...prev, ...entries]);
      }
      setHistorySkip(skip + 20);
    } catch { /* ignore */ }
    finally { setHistoryLoading(false); }
  }, [id, historyTab]);

  useEffect(() => {
    if (!expert) return;
    loadHistory(0);
  }, [expert, loadHistory]);

  async function enterEditMode() {
    if (!device) return;
    setEditName(device.name);
    setEditRoomId(device.roomId ?? '');
    setEditHost(device.settings?.host ?? '');
    setSaveError(null);
    try {
      const roomList = await getRooms();
      setRooms(roomList);
    } catch { /* use empty list */ }
    setEditing(true);
  }

  function cancelEdit() {
    setEditing(false);
    setSaveError(null);
  }

  async function saveEdit() {
    if (!id || !device) return;
    setSaving(true);
    setSaveError(null);
    try {
      const nameChanged = editName.trim() !== device.name;
      const roomChanged = editRoomId !== (device.roomId ?? '');
      const hostChanged = editHost.trim() !== (device.settings?.host ?? '');

      if (nameChanged || roomChanged) {
        const res = await fetch(`/api/devices/${id}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ name: editName.trim(), roomId: editRoomId }),
        });
        if (!res.ok) throw new Error(`Failed to update device: ${res.status}`);
      }

      if (hostChanged && editHost.trim()) {
        await updateDeviceSettings(id, { host: editHost.trim() });
      }

      setEditing(false);
      await loadDevice();
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : 'Failed to save');
    } finally {
      setSaving(false);
    }
  }

  async function cmd(capability: string, value: unknown) {
    if (!id) return;
    await sendCommand(id, { capability, value });
    await loadDevice();
  }

  if (loading) {
    return <div className="main-inner"><div style={{ color: 'var(--text-muted)', padding: 24, fontFamily: 'var(--font-body)' }}>Loading device…</div></div>;
  }

  if (error || !device) {
    return (
      <div className="main-inner">
      <div className="page-content">
        <button style={backBtnStyle} onClick={() => navigate(-1)}>← Back</button>
        <div style={{ color: 'var(--accent-red)' }}>{error ?? 'Device not found'}</div>
      </div>
      </div>
    );
  }

  const state = device.state ?? {};
  const isOffline = device.online === false;
  return (
    <div className="main-inner">
    <div className="page-content">
      <button
        style={backBtnStyle}
        onMouseEnter={(e) => (e.currentTarget.style.color = 'var(--accent-primary)')}
        onMouseLeave={(e) => (e.currentTarget.style.color = 'var(--text-muted)')}
        onClick={() => navigate(-1)}
      >
        ← All Devices
      </button>

      {/* Header — view or edit mode */}
      {!editing ? (
        <div style={{ marginBottom: isOffline ? 12 : 28 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 14, flexWrap: 'wrap' }}>
            <CapabilityIcon capability={primaryCapabilityIcon(device.capabilities)} size={28} />
            <div style={{
              fontFamily: 'var(--font-heading)', fontSize: 26, fontWeight: 700,
              color: 'var(--text-primary)', letterSpacing: '-0.02em',
            }}>
              {device.name}
            </div>
            <button
              onClick={enterEditMode}
              style={{
                padding: '5px 14px', fontSize: 12, fontWeight: 600, fontFamily: 'var(--font-body)',
                background: 'var(--bg-hover)', border: '1px solid var(--border-default)',
                borderRadius: 'var(--radius-sm)', color: 'var(--text-secondary)',
                cursor: 'pointer', transition: 'all 0.15s', letterSpacing: '0.02em',
              }}
              onMouseEnter={e => {
                e.currentTarget.style.borderColor = 'var(--accent-primary)';
                e.currentTarget.style.color = 'var(--accent-primary)';
              }}
              onMouseLeave={e => {
                e.currentTarget.style.borderColor = 'var(--border-default)';
                e.currentTarget.style.color = 'var(--text-secondary)';
              }}
            >
              Edit
            </button>
          </div>
          <div style={{ fontSize: 13, color: 'var(--text-muted)', marginTop: 6 }}>
            {device.communicationType}
            {device.capabilities.length > 0 && ' · '}
            {device.capabilities.join(' · ')}
          </div>
        </div>
      ) : (
        <div style={{
          background: 'var(--bg-elevated)', border: '1px solid var(--border-default)',
          borderRadius: 'var(--radius-md)', padding: '20px 22px', marginBottom: 28,
          boxShadow: 'var(--shadow-card)',
        }}>
          <div style={{
            fontSize: 10, fontWeight: 600, textTransform: 'uppercase' as const,
            letterSpacing: '0.08em', color: 'var(--accent-primary)', marginBottom: 18,
          }}>
            Edit Device
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            {/* Name */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
              <label style={fieldLabelStyle}>Name</label>
              <input
                type="text"
                value={editName}
                onChange={e => setEditName(e.target.value)}
                style={fieldInputStyle}
                onFocus={handleFocus}
                onBlur={handleBlur}
                autoFocus
              />
            </div>

            {/* Room */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
              <label style={fieldLabelStyle}>Room</label>
              <select
                value={editRoomId}
                onChange={e => setEditRoomId(e.target.value)}
                style={{ ...fieldInputStyle, appearance: 'none' as const }}
                onFocus={handleFocus}
                onBlur={handleBlur}
              >
                <option value="">— No room —</option>
                {rooms.map(r => <option key={r.id} value={r.id}>{r.name}</option>)}
              </select>
            </div>

            {/* IP / Host (Shelly only) */}
            {device.communicationType === 'shelly' && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
                <label style={fieldLabelStyle}>IP Address</label>
                <input
                  type="text"
                  value={editHost}
                  onChange={e => setEditHost(e.target.value)}
                  placeholder="e.g. 192.168.1.100"
                  style={fieldInputStyle}
                  onFocus={handleFocus}
                  onBlur={handleBlur}
                />
              </div>
            )}

            {saveError && (
              <div style={{ fontSize: 13, color: 'var(--accent-red)' }}>{saveError}</div>
            )}

            {/* Actions */}
            <div style={{ display: 'flex', gap: 10, marginTop: 4 }}>
              <button
                className="btn-primary"
                disabled={saving || !editName.trim()}
                style={{ opacity: saving || !editName.trim() ? 0.5 : 1 }}
                onClick={saveEdit}
              >
                {saving ? 'Saving…' : 'Save'}
              </button>
              <button className="btn-secondary" onClick={cancelEdit}>Cancel</button>
              <button
                style={{
                  marginLeft: 'auto', padding: '8px 16px', borderRadius: 'var(--radius-sm)',
                  fontSize: 13, fontWeight: 600, cursor: 'pointer',
                  background: 'transparent', border: '1px solid var(--accent-red)',
                  color: 'var(--accent-red)', fontFamily: 'var(--font-body)',
                }}
                onClick={async () => {
                  if (!id || !confirm('Delete this device? It will need to be re-discovered and configured.')) return;
                  await deleteDevice(id);
                  navigate('/');
                }}
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Offline banner */}
      {isOffline && (
        <div style={{
          background: 'color-mix(in srgb, var(--accent-red) 12%, transparent)',
          border: '1px solid color-mix(in srgb, var(--accent-red) 35%, transparent)',
          borderRadius: 'var(--radius-sm)', padding: '10px 14px', marginBottom: 28,
          fontSize: 13, color: 'var(--accent-red)', fontWeight: 500,
        }}>
          Device is offline — check network connection
        </div>
      )}

      {/* Capability cards */}
      {device.capabilities.length > 0 && (
        <div style={{
          display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(min(220px, 100%), 1fr))',
          gap: 14, marginBottom: 28,
        }}>
          {device.capabilities.map(cap => renderCapabilityCard(cap, state, cmd, device))}
        </div>
      )}

      {/* Expert Mode: raw state, extras, settings */}
      {expert && (
        <div style={{
          background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)',
          borderRadius: 'var(--radius-md)', padding: '16px 18px', marginTop: 14,
          boxShadow: 'var(--shadow-card)', fontFamily: 'monospace',
        }}>
          <div style={{
            fontSize: 10, fontWeight: 600, textTransform: 'uppercase' as const,
            letterSpacing: '0.08em', color: 'var(--accent-primary)', marginBottom: 14,
          }}>
            Expert View
          </div>

          {state['Extras'] != null && typeof state['Extras'] === 'object' && (
            <div style={{ marginBottom: 16 }}>
              <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--text-muted)', marginBottom: 6, textTransform: 'uppercase' as const, letterSpacing: '0.06em', fontFamily: 'var(--font-body)' }}>
                Raw Data
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                {Object.entries(state['Extras'] as Record<string, unknown>).map(([k, v]) => (
                  <div key={k} style={{ display: 'flex', gap: 10, fontSize: 12 }}>
                    <span style={{ color: 'var(--text-muted)', minWidth: 180, flexShrink: 0 }}>{k}</span>
                    <span style={{ color: 'var(--text-secondary)', wordBreak: 'break-all' }}>
                      {typeof v === 'string' && (v.startsWith('{') || v.startsWith('[')) ? v : String(v)}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          )}

          <div style={{ marginBottom: 16 }}>
            <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--text-muted)', marginBottom: 6, textTransform: 'uppercase' as const, letterSpacing: '0.06em', fontFamily: 'var(--font-body)' }}>
              Capabilities
            </div>
            <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
              {device.capabilities.join(', ')}
            </div>
          </div>

          <div style={{ marginBottom: 16 }}>
            <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--text-muted)', marginBottom: 6, textTransform: 'uppercase' as const, letterSpacing: '0.06em', fontFamily: 'var(--font-body)' }}>
              Full State
            </div>
            <pre style={{ fontSize: 11, color: 'var(--text-secondary)', overflow: 'auto', margin: 0 }}>
              {JSON.stringify(state, null, 2)}
            </pre>
          </div>

          {device.settings && Object.keys(device.settings).length > 0 && (
            <div>
              <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--text-muted)', marginBottom: 6, textTransform: 'uppercase' as const, letterSpacing: '0.06em', fontFamily: 'var(--font-body)' }}>
                Settings / Metadata
              </div>
              <pre style={{ fontSize: 11, color: 'var(--text-secondary)', overflow: 'auto', margin: 0 }}>
                {JSON.stringify(device.settings, null, 2)}
              </pre>
            </div>
          )}
        </div>
      )}

      {/* History section (expert mode only) */}
      {expert && (
        <div style={{
          background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)',
          borderRadius: 'var(--radius-md)', padding: '16px 18px', marginTop: 14,
          boxShadow: 'var(--shadow-card)',
        }}>
          <div style={{
            fontSize: 10, fontWeight: 600, textTransform: 'uppercase' as const,
            letterSpacing: '0.08em', color: 'var(--accent-primary)', marginBottom: 14,
            fontFamily: 'var(--font-body)',
          }}>
            History
          </div>

          {/* Tabs */}
          <div style={{ display: 'flex', gap: 4, marginBottom: 14 }}>
            {(['state', 'commands'] as const).map(tab => (
              <button
                key={tab}
                onClick={() => setHistoryTab(tab)}
                style={{
                  padding: '5px 14px', fontSize: 12, fontWeight: 600,
                  fontFamily: 'var(--font-body)', cursor: 'pointer',
                  borderRadius: 'var(--radius-sm)', transition: 'all 0.15s',
                  background: historyTab === tab ? 'var(--accent-primary-dim)' : 'var(--bg-hover)',
                  border: `1px solid ${historyTab === tab ? 'var(--accent-primary)' : 'var(--border-default)'}`,
                  color: historyTab === tab ? 'var(--accent-primary)' : 'var(--text-secondary)',
                  textTransform: 'capitalize' as const,
                }}
              >
                {tab}
              </button>
            ))}
          </div>

          {/* Table */}
          <div style={{ overflowX: 'auto' }}>
            <table style={{
              width: '100%', borderCollapse: 'collapse',
              fontFamily: 'monospace', fontSize: 12,
            }}>
              <thead>
                <tr style={{ borderBottom: '1px solid var(--border-subtle)' }}>
                  <th style={historyThStyle}>Timestamp</th>
                  <th style={historyThStyle}>Capability</th>
                  <th style={historyThStyle}>Value</th>
                  {historyTab === 'commands' && <th style={historyThStyle}>Source</th>}
                </tr>
              </thead>
              <tbody>
                {historyTab === 'state'
                  ? stateHistory.map((e, i) => (
                    <tr key={i} style={{ borderBottom: '1px solid var(--border-subtle)' }}>
                      <td style={historyTdStyle}>{formatTimeAgo(e.timestamp)}</td>
                      <td style={{ ...historyTdStyle, color: 'var(--accent-primary)' }}>{e.capability}</td>
                      <td style={historyTdStyle}>{formatHistoryValue(e.value)}</td>
                    </tr>
                  ))
                  : commandHistory.map((e, i) => (
                    <tr key={i} style={{ borderBottom: '1px solid var(--border-subtle)' }}>
                      <td style={historyTdStyle}>{formatTimeAgo(e.timestamp)}</td>
                      <td style={{ ...historyTdStyle, color: 'var(--accent-primary)' }}>{e.capability}</td>
                      <td style={historyTdStyle}>{formatHistoryValue(e.value)}</td>
                      <td style={{ ...historyTdStyle, color: 'var(--text-muted)' }}>{e.source ?? '—'}</td>
                    </tr>
                  ))
                }
                {(historyTab === 'state' ? stateHistory : commandHistory).length === 0 && !historyLoading && (
                  <tr>
                    <td colSpan={historyTab === 'commands' ? 4 : 3} style={{ ...historyTdStyle, color: 'var(--text-muted)', textAlign: 'center', padding: '16px 0' }}>
                      No history yet
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>

          {/* Load More */}
          <div style={{ marginTop: 12, display: 'flex', alignItems: 'center', gap: 12 }}>
            <button
              className="btn-secondary"
              disabled={historyLoading}
              onClick={() => loadHistory(historySkip)}
              style={{ fontSize: 12, padding: '6px 14px', opacity: historyLoading ? 0.5 : 1 }}
            >
              {historyLoading ? 'Loading…' : 'Load More'}
            </button>
          </div>
        </div>
      )}
    </div>
    </div>
  );
}

// --- History helpers ---

function formatTimeAgo(ts: string): string {
  const diff = Date.now() - new Date(ts).getTime();
  if (diff < 60000) return `${Math.floor(diff / 1000)}s ago`;
  if (diff < 3600000) return `${Math.floor(diff / 60000)}m ago`;
  if (diff < 86400000) return `${Math.floor(diff / 3600000)}h ago`;
  return new Date(ts).toLocaleString();
}

function formatHistoryValue(v: unknown): string {
  if (v === null || v === undefined) return '—';
  if (typeof v === 'object') return JSON.stringify(v);
  return String(v);
}

const historyThStyle: React.CSSProperties = {
  padding: '6px 10px', textAlign: 'left' as const, fontSize: 10,
  fontWeight: 600, textTransform: 'uppercase' as const, letterSpacing: '0.06em',
  color: 'var(--text-muted)', fontFamily: 'var(--font-body)',
};

const historyTdStyle: React.CSSProperties = {
  padding: '6px 10px', color: 'var(--text-secondary)', verticalAlign: 'middle',
};

// --- Shared styles ---

const backBtnStyle: React.CSSProperties = {
  display: 'inline-flex', alignItems: 'center', gap: 6,
  color: 'var(--text-muted)', fontSize: 13, cursor: 'pointer',
  marginBottom: 22, fontFamily: 'var(--font-body)', transition: 'color 0.15s',
};

const fieldLabelStyle: React.CSSProperties = {
  fontSize: 11, fontWeight: 600, color: 'var(--text-muted)',
  textTransform: 'uppercase', letterSpacing: '0.06em',
};

const fieldInputStyle: React.CSSProperties = {
  background: 'var(--bg-surface)', border: '1px solid var(--border-default)',
  borderRadius: 'var(--radius-sm)', padding: '9px 13px',
  color: 'var(--text-primary)', fontFamily: 'var(--font-body)',
  fontSize: 14, outline: 'none', transition: 'border-color 0.15s, box-shadow 0.15s',
  width: '100%',
};

function handleFocus(e: React.FocusEvent<HTMLInputElement | HTMLSelectElement>) {
  e.currentTarget.style.borderColor = 'var(--accent-primary)';
  e.currentTarget.style.boxShadow = '0 0 0 3px color-mix(in srgb, var(--accent-primary) 20%, transparent)';
}

function handleBlur(e: React.FocusEvent<HTMLInputElement | HTMLSelectElement>) {
  e.currentTarget.style.borderColor = 'var(--border-default)';
  e.currentTarget.style.boxShadow = 'none';
}

// --- Camera snapshot component ---

function CameraSnapshot({ deviceId }: { deviceId: string }) {
  const [refreshKey, setRefreshKey] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  const src = `/api/devices/${deviceId}/snapshot?t=${refreshKey}`;

  return (
    <div>
      <div style={{
        position: 'relative',
        background: 'var(--bg-hover)',
        borderRadius: 'var(--radius-sm)',
        overflow: 'hidden',
        minHeight: 180,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
      }}>
        {loading && !error && (
          <span style={{ position: 'absolute', color: 'var(--text-muted)', fontSize: 13 }}>Loading snapshot...</span>
        )}
        {error && (
          <span style={{ color: 'var(--accent-red)', fontSize: 13 }}>Failed to load snapshot</span>
        )}
        <img
          key={refreshKey}
          src={src}
          alt="Camera snapshot"
          onLoad={() => { setLoading(false); setError(false); }}
          onError={() => { setLoading(false); setError(true); }}
          style={{
            width: '100%',
            display: error ? 'none' : 'block',
            borderRadius: 'var(--radius-sm)',
          }}
        />
      </div>
      <button
        className="btn-secondary"
        onClick={() => { setLoading(true); setError(false); setRefreshKey(k => k + 1); }}
        style={{ marginTop: 10, fontSize: 12, padding: '6px 14px' }}
      >
        Refresh Snapshot
      </button>
    </div>
  );
}

// --- Capability card renderer (extracted to keep component readable) ---

function capAccentColor(cap: string): string {
  switch (cap) {
    case 'Switch': case 'Dimmer': case 'Light': return 'var(--accent-primary)';
    case 'Cover': return 'var(--accent-teal)';
    case 'Temperature': return 'var(--accent-red)';
    case 'Motion': return 'var(--accent-green)';
    case 'Power': return 'var(--accent-blue)';
    case 'Energy': return 'var(--accent-green)';
    case 'Humidity': return 'var(--accent-blue)';
    case 'Camera': return 'var(--accent-blue)';
    default: return 'var(--accent-primary)';
  }
}

const capCardStyle: React.CSSProperties = {
  background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)',
  borderRadius: 'var(--radius-md)', padding: '16px 18px',
  position: 'relative', overflow: 'hidden',
  boxShadow: 'var(--shadow-card)', transition: 'border-color 0.2s, box-shadow 0.2s',
};

const capLabelStyle: React.CSSProperties = {
  fontSize: 10, fontWeight: 600, textTransform: 'uppercase',
  letterSpacing: '0.08em', color: 'var(--text-muted)', marginBottom: 10,
  display: 'flex', alignItems: 'center', gap: 6,
};

function capValueStyle(color: string = 'var(--text-primary)'): React.CSSProperties {
  return { fontFamily: 'var(--font-heading)', fontSize: 22, fontWeight: 600, color, marginBottom: 2 };
}

function Indicator({ color }: { color: string }) {
  return <div style={{ position: 'absolute', top: 0, left: 0, width: '100%', height: 2, background: color, opacity: 0.7 }} />;
}

const coverBtnStyle: React.CSSProperties = {
  flex: 1, padding: '10px 0', borderRadius: 'var(--radius-sm)',
  border: '1px solid var(--border-default)', background: 'var(--bg-hover)',
  color: 'var(--text-primary)', fontFamily: 'var(--font-body)',
  fontSize: 14, fontWeight: 600, cursor: 'pointer', transition: 'all 0.15s',
};

function renderCapabilityCard(
  cap: string,
  state: Record<string, unknown>,
  cmd: (capability: string, value: unknown) => void,
  device: Device,
) {
  const accent = capAccentColor(cap);

  switch (cap) {
    case 'Switch': {
      const isOn = Boolean(state['Switch']);
      return (
        <div key={cap} style={capCardStyle}>
          <Indicator color={accent} />
          <div style={capLabelStyle}><CapabilityIcon capability="Switch" size={13} />Switch</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginTop: 6 }}>
            <ToggleSwitch checked={isOn} onChange={v => cmd('Switch', v)} />
            <span style={{ fontSize: 14, fontWeight: 500, color: isOn ? 'var(--accent-primary)' : 'var(--text-muted)' }}>
              {isOn ? 'On' : 'Off'}
            </span>
          </div>
        </div>
      );
    }
    case 'Dimmer': {
      const level = typeof state['Dimmer'] === 'number' ? (state['Dimmer'] as number) : 0;
      return (
        <div key={cap} style={capCardStyle}>
          <Indicator color={accent} />
          <div style={capLabelStyle}><CapabilityIcon capability="Dimmer" size={13} />Dimmer</div>
          <div style={capValueStyle('var(--accent-primary)')}>{Math.round(level)}%</div>
          <div style={{ marginTop: 12 }}>
            <SliderControl value={level} className="slider-dimmer" accentColor="var(--accent-primary)" onCommit={v => cmd('Dimmer', v)} />
          </div>
        </div>
      );
    }
    case 'Light': {
      const lightState = state['Light'] as Record<string, unknown> | undefined;
      const isOn = lightState?.on === true;
      const brightness = typeof lightState?.brightness === 'number' ? (lightState.brightness as number) : 0;
      const hasBrightness = lightState?.brightness !== undefined;
      const colorTemp = typeof lightState?.color_temp === 'number' ? (lightState.color_temp as number) : 0;
      const hasColorTemp = lightState?.color_temp !== undefined;
      const colorH = typeof lightState?.color_h === 'number' ? (lightState.color_h as number) : 0;
      const colorS = typeof lightState?.color_s === 'number' ? (lightState.color_s as number) : 0;
      const hasColor = lightState?.color_x !== undefined || lightState?.color_h !== undefined;
      const colorMode = lightState?.color_mode as string | undefined;
      return (
        <div key={cap} style={{ ...capCardStyle, gridColumn: (hasColor || hasColorTemp) ? 'span 2' : undefined }}>
          <Indicator color={accent} />
          <div style={capLabelStyle}><CapabilityIcon capability="Light" size={13} />Light</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginTop: 6 }}>
            <ToggleSwitch checked={isOn} onChange={v => cmd('Light', v)} />
            <span style={{ fontSize: 14, fontWeight: 500, color: isOn ? 'var(--accent-primary)' : 'var(--text-muted)' }}>
              {isOn ? (hasBrightness ? `${Math.round(brightness)}%` : 'On') : 'Off'}
            </span>
          </div>
          {hasBrightness && (
            <div style={{ marginTop: 14 }}>
              <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase' as const, letterSpacing: '0.06em', color: 'var(--text-muted)', marginBottom: 6 }}>Brightness</div>
              <SliderControl value={brightness} className="slider-dimmer" accentColor="var(--accent-primary)" onCommit={v => cmd('Light', v)} />
            </div>
          )}
          <div style={{ display: 'flex', gap: 20, marginTop: 16, flexWrap: 'wrap' }}>
            {hasColor && (
              <div>
                <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase' as const, letterSpacing: '0.06em', color: colorMode === 'xy' || colorMode === 'hs' ? 'var(--accent-primary)' : 'var(--text-muted)', marginBottom: 8 }}>Color</div>
                <ColorWheel
                  hue={colorH}
                  saturation={colorS}
                  onCommit={(h, s) => cmd('Light', JSON.stringify({ color: { hue: h, saturation: s } }))}
                />
              </div>
            )}
            {hasColorTemp && (
              <div style={{ flex: 1, minWidth: 180 }}>
                <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase' as const, letterSpacing: '0.06em', color: colorMode === 'color_temp' ? 'var(--accent-primary)' : 'var(--text-muted)', marginBottom: 8 }}>Color Temperature</div>
                <ColorTempSlider
                  value={colorTemp}
                  onCommit={v => cmd('Light', JSON.stringify({ color_temp: v }))}
                />
              </div>
            )}
          </div>
        </div>
      );
    }
    case 'Cover': {
      const pos = typeof state['Cover'] === 'number' ? (state['Cover'] as number) : 0;
      return (
        <div key={cap} style={capCardStyle}>
          <Indicator color={accent} />
          <div style={capLabelStyle}><CapabilityIcon capability="Cover" size={13} />Cover</div>
          <div style={capValueStyle('var(--accent-teal)')}>{Math.round(pos)}%</div>
          <div style={{ display: 'flex', gap: 8, marginTop: 14 }}>
            {[['Close', 0], ['50%', 50], ['Open', 100]].map(([label, val]) => (
              <button
                key={label as string}
                style={coverBtnStyle}
                onMouseEnter={e => { e.currentTarget.style.background = 'var(--accent-teal-dim)'; e.currentTarget.style.borderColor = 'var(--accent-teal)'; }}
                onMouseLeave={e => { e.currentTarget.style.background = 'var(--bg-hover)'; e.currentTarget.style.borderColor = 'var(--border-default)'; }}
                onClick={() => cmd('Cover', val)}
              >
                {label}
              </button>
            ))}
          </div>
          <div style={{ marginTop: 12 }}>
            <SliderControl value={pos} className="slider-cover" accentColor="var(--accent-teal)" onCommit={v => cmd('Cover', v)} />
          </div>
        </div>
      );
    }
    case 'Temperature': {
      const temp = state['Temperature'];
      return (
        <div key={cap} style={capCardStyle}>
          <Indicator color={accent} />
          <div style={capLabelStyle}><CapabilityIcon capability="Temperature" size={13} />Temperature</div>
          <div style={capValueStyle(temp != null ? 'var(--accent-red)' : 'var(--text-muted)')}>
            {temp != null ? `${Number(temp).toFixed(1)} °C` : '— °C'}
          </div>
        </div>
      );
    }
    case 'Motion': {
      const detected = Boolean(state['Motion']);
      return (
        <div key={cap} style={capCardStyle}>
          <Indicator color={accent} />
          <div style={capLabelStyle}><CapabilityIcon capability="Motion" size={13} />Motion</div>
          <div style={{ marginTop: 8 }}>
            <StatusDot active={detected} label={detected ? 'Detected' : 'Clear'} />
          </div>
        </div>
      );
    }
    case 'Power': {
      const power = state['Power'];
      return (
        <div key={cap} style={capCardStyle}>
          <Indicator color={accent} />
          <div style={capLabelStyle}><CapabilityIcon capability="Power" size={13} />Power</div>
          <div style={capValueStyle(power != null ? 'var(--accent-blue)' : 'var(--text-muted)')}>
            {power != null ? `${Number(power).toFixed(1)} W` : '— W'}
          </div>
        </div>
      );
    }
    case 'Energy': {
      const energy = state['Energy'];
      return (
        <div key={cap} style={capCardStyle}>
          <Indicator color={accent} />
          <div style={capLabelStyle}><CapabilityIcon capability="Energy" size={13} />Energy</div>
          <div style={capValueStyle(energy != null ? 'var(--accent-green)' : 'var(--text-muted)')}>
            {energy != null ? `${Number(energy).toFixed(2)} kWh` : '— kWh'}
          </div>
        </div>
      );
    }
    case 'Humidity': {
      const hum = typeof state['Humidity'] === 'number' ? (state['Humidity'] as number) : 0;
      return (
        <div key={cap} style={capCardStyle}>
          <Indicator color={accent} />
          <div style={capLabelStyle}><CapabilityIcon capability="Humidity" size={13} />Humidity</div>
          <div style={{ ...capValueStyle('var(--accent-blue)'), marginBottom: 12 }}>{Math.round(hum)}%</div>
          <ProgressBar value={hum} color="var(--accent-blue)" />
        </div>
      );
    }
    case 'Action': {
      const lastAction = state['Action'] as string | undefined;
      const actionValues = device.settings?.action_values?.split(',').filter(Boolean) ?? [];
      return (
        <div key={cap} style={{ ...capCardStyle, gridColumn: actionValues.length > 5 ? 'span 2' : undefined }}>
          <Indicator color={accent} />
          <div style={capLabelStyle}><CapabilityIcon capability="Action" size={13} />Last Action</div>
          <div style={capValueStyle(lastAction ? 'var(--accent-primary)' : 'var(--text-muted)')}>
            {lastAction ?? '—'}
          </div>
          {actionValues.length > 0 && (
            <div style={{ marginTop: 12 }}>
              <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase' as const, letterSpacing: '0.06em', color: 'var(--text-muted)', marginBottom: 8 }}>
                Available Actions
              </div>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                {actionValues.map(a => (
                  <span key={a} style={{
                    display: 'inline-block', padding: '3px 8px', borderRadius: 4, fontSize: 11, fontWeight: 500,
                    backgroundColor: a === lastAction ? 'var(--accent-primary-dim)' : 'var(--bg-hover)',
                    color: a === lastAction ? 'var(--accent-primary)' : 'var(--text-secondary)',
                    border: `1px solid ${a === lastAction ? 'var(--accent-primary)' : 'var(--border-subtle)'}`,
                  }}>
                    {a}
                  </span>
                ))}
              </div>
            </div>
          )}
        </div>
      );
    }
    case 'Camera': {
      const rtspUrl = state['Camera'] as string | undefined;
      return (
        <div key={cap} style={{ ...capCardStyle, gridColumn: 'span 2' }}>
          <Indicator color="var(--accent-blue)" />
          <div style={capLabelStyle}><CapabilityIcon capability="Camera" size={13} />Camera</div>
          <CameraSnapshot deviceId={device.id} />
          {rtspUrl && (
            <div style={{ marginTop: 12 }}>
              <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase' as const, letterSpacing: '0.06em', color: 'var(--text-muted)', marginBottom: 6 }}>
                RTSP Stream
              </div>
              <span
                role="button"
                title="Click to copy"
                onClick={() => navigator.clipboard.writeText(rtspUrl)}
                style={{
                  fontSize: 12, fontFamily: 'monospace', color: 'var(--text-secondary)',
                  cursor: 'pointer', padding: '4px 10px', borderRadius: 6,
                  background: 'var(--bg-hover)', border: '1px solid var(--border-subtle)',
                  transition: 'border-color 0.15s, color 0.15s', display: 'inline-block',
                  wordBreak: 'break-all' as const,
                }}
                onMouseEnter={e => { e.currentTarget.style.borderColor = 'var(--accent-primary)'; e.currentTarget.style.color = 'var(--accent-primary)'; }}
                onMouseLeave={e => { e.currentTarget.style.borderColor = 'var(--border-subtle)'; e.currentTarget.style.color = 'var(--text-secondary)'; }}
              >
                {rtspUrl}
              </span>
            </div>
          )}
        </div>
      );
    }
    case 'Presence': {
      const present = state['Presence'] === true;
      const ip = device.settings?.ip;
      return (
        <div key={cap} style={capCardStyle}>
          <Indicator color="var(--accent-green)" />
          <div style={capLabelStyle}><CapabilityIcon capability="Presence" size={13} />Presence</div>
          <div style={capValueStyle(present ? 'var(--accent-green)' : 'var(--text-muted)')}>
            {present ? 'Home' : 'Away'}
          </div>
          {ip && (
            <div style={{ marginTop: 10 }}>
              <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase' as const, letterSpacing: '0.06em', color: 'var(--text-muted)', marginBottom: 6 }}>
                IP Address
              </div>
              <span
                role="button"
                title="Click to copy"
                onClick={() => navigator.clipboard.writeText(ip)}
                style={{
                  fontSize: 14, fontFamily: 'monospace', color: 'var(--text-secondary)',
                  cursor: 'pointer', padding: '4px 10px', borderRadius: 6,
                  background: 'var(--bg-hover)', border: '1px solid var(--border-subtle)',
                  transition: 'border-color 0.15s, color 0.15s', display: 'inline-block',
                }}
                onMouseEnter={e => { e.currentTarget.style.borderColor = 'var(--accent-primary)'; e.currentTarget.style.color = 'var(--accent-primary)'; }}
                onMouseLeave={e => { e.currentTarget.style.borderColor = 'var(--border-subtle)'; e.currentTarget.style.color = 'var(--text-secondary)'; }}
              >
                {ip}
              </span>
            </div>
          )}
        </div>
      );
    }
    default: {
      const val = state[cap];
      return (
        <div key={cap} style={capCardStyle}>
          <Indicator color={accent} />
          <div style={capLabelStyle}>{cap}</div>
          <div style={capValueStyle()}>{val != null ? String(val) : '—'}</div>
        </div>
      );
    }
  }
}
