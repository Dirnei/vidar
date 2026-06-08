import React, { useCallback, useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import type { Device, DeviceGroup, Room } from '../types';
import {
  getGroup,
  getRooms,
  getDevices,
  updateGroup,
  deleteGroup,
  sendGroupCommand,
  getDevice,
} from '../api/client';
import { subscribeDeviceState } from '../api/sse';
import { ToggleSwitch } from '../components/ToggleSwitch';
import { ProgressBar } from '../components/ProgressBar';
import { StatusDot } from '../components/StatusDot';
import { SliderControl } from '../components/SliderControl';
import { CapabilityIcon } from '../components/CapabilityIcon';
import { DeviceRow } from '../components/DeviceRow';

export function GroupDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [group, setGroup] = useState<DeviceGroup | null>(null);
  const [memberDevices, setMemberDevices] = useState<Device[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Edit mode
  const [editing, setEditing] = useState(false);
  const [editName, setEditName] = useState('');
  const [editRoomId, setEditRoomId] = useState('');
  const [editDeviceIds, setEditDeviceIds] = useState<Set<string>>(new Set());
  const [rooms, setRooms] = useState<Room[]>([]);
  const [allDevices, setAllDevices] = useState<Device[]>([]);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);

  const loadGroup = useCallback(async () => {
    if (!id) return;
    try {
      const g = await getGroup(id);
      setGroup(g);
      // Load member devices
      const members = await Promise.all(g.deviceIds.map((did) => getDevice(did).catch(() => null)));
      setMemberDevices(members.filter((d): d is Device => d !== null));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load group');
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    loadGroup();
    const unsub = subscribeDeviceState(() => loadGroup());
    return unsub;
  }, [loadGroup]);

  async function enterEditMode() {
    if (!group) return;
    setEditName(group.name);
    setEditRoomId(group.roomId);
    setEditDeviceIds(new Set(group.deviceIds));
    setSaveError(null);
    try {
      const [roomList, deviceList] = await Promise.all([getRooms(), getDevices()]);
      setRooms(roomList);
      setAllDevices(deviceList);
    } catch { /* use empty lists */ }
    setEditing(true);
  }

  function cancelEdit() {
    setEditing(false);
    setSaveError(null);
  }

  async function saveEdit() {
    if (!id || !group) return;
    setSaving(true);
    setSaveError(null);
    try {
      await updateGroup(id, {
        name: editName.trim(),
        roomId: editRoomId,
        deviceIds: Array.from(editDeviceIds),
      });
      setEditing(false);
      await loadGroup();
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : 'Failed to save');
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete() {
    if (!id || !window.confirm('Delete this group? Devices will not be deleted.')) return;
    setDeleting(true);
    try {
      await deleteGroup(id);
      navigate(-1);
    } catch (e) {
      setDeleting(false);
      alert(e instanceof Error ? e.message : 'Failed to delete group');
    }
  }

  function toggleEditDevice(deviceId: string) {
    setEditDeviceIds((prev) => {
      const next = new Set(prev);
      if (next.has(deviceId)) {
        next.delete(deviceId);
      } else {
        next.add(deviceId);
      }
      return next;
    });
  }

  // Devices in selected room available for this group (ungrouped OR already in this group)
  const availableDevices = allDevices.filter(
    (d) => d.roomId === editRoomId && (!d.groupId || d.groupId === id)
  );

  async function cmd(capability: string, value: unknown) {
    if (!id) return;
    await sendGroupCommand(id, { capability, value });
    await loadGroup();
  }

  if (loading) {
    return <div className="main-inner"><div style={{ color: 'var(--text-muted)', padding: 24, fontFamily: 'var(--font-body)' }}>Loading group…</div></div>;
  }

  if (error || !group) {
    return (
      <div className="main-inner">
      <div className="page-content">
        <button style={backBtnStyle} onClick={() => navigate(-1)}>← Rooms</button>
        <div style={{ color: 'var(--accent-red)' }}>{error ?? 'Group not found'}</div>
      </div>
      </div>
    );
  }

  const state = group.state ?? {};
  const isOffline = group.online === false;

  return (
    <div className="main-inner">
    <div className="page-content">
      <button
        style={backBtnStyle}
        onMouseEnter={(e) => (e.currentTarget.style.color = 'var(--accent-primary)')}
        onMouseLeave={(e) => (e.currentTarget.style.color = 'var(--text-muted)')}
        onClick={() => navigate(-1)}
      >
        ← Rooms
      </button>

      {/* Header — view or edit mode */}
      {!editing ? (
        <div style={{ marginBottom: isOffline ? 12 : 28 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
            <GroupIcon size={28} />
            <div style={{
              fontFamily: 'var(--font-heading)', fontSize: 26, fontWeight: 700,
              color: 'var(--text-primary)', letterSpacing: '-0.02em',
            }}>
              {group.name}
            </div>
            <button
              onClick={enterEditMode}
              style={{
                padding: '5px 14px', fontSize: 12, fontWeight: 600, fontFamily: 'var(--font-body)',
                background: 'var(--bg-hover)', border: '1px solid var(--border-default)',
                borderRadius: 'var(--radius-sm)', color: 'var(--text-secondary)',
                cursor: 'pointer', transition: 'all 0.15s', letterSpacing: '0.02em',
              }}
              onMouseEnter={e => { e.currentTarget.style.borderColor = 'var(--accent-primary)'; e.currentTarget.style.color = 'var(--accent-primary)'; }}
              onMouseLeave={e => { e.currentTarget.style.borderColor = 'var(--border-default)'; e.currentTarget.style.color = 'var(--text-secondary)'; }}
            >
              Edit
            </button>
            <button
              onClick={handleDelete}
              disabled={deleting}
              style={{
                padding: '5px 14px', fontSize: 12, fontWeight: 600, fontFamily: 'var(--font-body)',
                background: 'var(--bg-hover)', border: '1px solid var(--border-default)',
                borderRadius: 'var(--radius-sm)', color: 'var(--text-muted)',
                cursor: 'pointer', transition: 'all 0.15s', letterSpacing: '0.02em',
                opacity: deleting ? 0.5 : 1,
              }}
              onMouseEnter={e => { e.currentTarget.style.borderColor = 'var(--accent-red)'; e.currentTarget.style.color = 'var(--accent-red)'; }}
              onMouseLeave={e => { e.currentTarget.style.borderColor = 'var(--border-default)'; e.currentTarget.style.color = 'var(--text-muted)'; }}
            >
              {deleting ? 'Deleting…' : 'Delete'}
            </button>
          </div>
          <div style={{ fontSize: 13, color: 'var(--text-muted)', marginTop: 6 }}>
            {group.roomName ?? 'No room'}
            {' · '}
            {group.deviceIds.length} device{group.deviceIds.length !== 1 ? 's' : ''}
            {group.capabilities.length > 0 && ' · ' + group.capabilities.join(' · ')}
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
            Edit Group
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
                onChange={e => { setEditRoomId(e.target.value); setEditDeviceIds(new Set()); }}
                style={{ ...fieldInputStyle, appearance: 'none' as const }}
                onFocus={handleFocus}
                onBlur={handleBlur}
              >
                {rooms.map(r => <option key={r.id} value={r.id}>{r.name}</option>)}
              </select>
            </div>

            {/* Member devices */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
              <label style={fieldLabelStyle}>Member Devices</label>
              {availableDevices.length === 0 ? (
                <div style={{ fontSize: 13, color: 'var(--text-muted)' }}>No devices available in this room.</div>
              ) : (
                <div style={{
                  border: '1px solid var(--border-default)',
                  borderRadius: 'var(--radius-sm)',
                  overflow: 'hidden',
                  maxHeight: 200,
                  overflowY: 'auto',
                }}>
                  {availableDevices.map((d, i) => (
                    <label
                      key={d.id}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 10,
                        padding: '9px 13px',
                        cursor: 'pointer',
                        borderTop: i > 0 ? '1px solid var(--border-subtle)' : 'none',
                        background: editDeviceIds.has(d.id) ? 'var(--accent-primary-dim)' : 'var(--bg-hover)',
                        transition: 'background 0.15s',
                        fontSize: 14,
                      }}
                    >
                      <input
                        type="checkbox"
                        checked={editDeviceIds.has(d.id)}
                        onChange={() => toggleEditDevice(d.id)}
                        style={{ accentColor: 'var(--accent-primary)', width: 15, height: 15, flexShrink: 0 }}
                      />
                      <span style={{ color: 'var(--text-primary)' }}>{d.name}</span>
                      <span style={{ marginLeft: 'auto', fontSize: 11, color: 'var(--text-muted)' }}>
                        {d.capabilities.join(' · ')}
                      </span>
                    </label>
                  ))}
                </div>
              )}
            </div>

            {saveError && (
              <div style={{ fontSize: 13, color: 'var(--accent-red)' }}>{saveError}</div>
            )}

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
          Some or all devices in this group are offline
        </div>
      )}

      {/* Capability cards */}
      {group.capabilities.length > 0 && (
        <div style={{
          display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
          gap: 14, marginBottom: 28,
        }}>
          {group.capabilities.map(cap => renderCapabilityCard(cap, state, cmd))}
        </div>
      )}

      {/* Members section */}
      {memberDevices.length > 0 && (
        <div>
          <div style={{
            fontSize: 10, fontWeight: 600, textTransform: 'uppercase' as const,
            letterSpacing: '0.08em', color: 'var(--text-muted)',
            marginBottom: 10,
          }}>
            Members
          </div>
          <div style={{
            background: 'var(--bg-elevated)',
            border: '1px solid var(--border-subtle)',
            borderRadius: 'var(--radius-lg)',
            padding: '4px 20px',
            boxShadow: 'var(--shadow-card)',
          }}>
            {memberDevices.map((d) => (
              <DeviceRow key={d.id} device={d} onStateChange={loadGroup} />
            ))}
          </div>
        </div>
      )}
    </div>
    </div>
  );
}

// --- GroupIcon ---

function GroupIcon({ size = 20 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="var(--accent-primary)" strokeWidth="2">
      <rect x="3" y="3" width="14" height="14" rx="2" />
      <rect x="7" y="7" width="14" height="14" rx="2" />
    </svg>
  );
}

// --- Shared styles ---

const backBtnStyle: React.CSSProperties = {
  display: 'inline-flex', alignItems: 'center', gap: 6,
  color: 'var(--text-muted)', fontSize: 13, cursor: 'pointer',
  marginBottom: 22, fontFamily: 'var(--font-body)', transition: 'color 0.15s',
  background: 'none', border: 'none',
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
  maxWidth: 400,
};

function handleFocus(e: React.FocusEvent<HTMLInputElement | HTMLSelectElement>) {
  e.currentTarget.style.borderColor = 'var(--accent-primary)';
  e.currentTarget.style.boxShadow = '0 0 0 3px color-mix(in srgb, var(--accent-primary) 20%, transparent)';
}

function handleBlur(e: React.FocusEvent<HTMLInputElement | HTMLSelectElement>) {
  e.currentTarget.style.borderColor = 'var(--border-default)';
  e.currentTarget.style.boxShadow = 'none';
}

// --- Capability card renderer ---

function capAccentColor(cap: string): string {
  switch (cap) {
    case 'Switch': case 'Dimmer': case 'Light': return 'var(--accent-primary)';
    case 'Cover': return 'var(--accent-teal)';
    case 'Temperature': return 'var(--accent-red)';
    case 'Motion': return 'var(--accent-green)';
    case 'Power': return 'var(--accent-blue)';
    case 'Energy': return 'var(--accent-green)';
    case 'Humidity': return 'var(--accent-blue)';
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
      return (
        <div key={cap} style={capCardStyle}>
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
