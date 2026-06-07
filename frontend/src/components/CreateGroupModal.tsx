import React, { useEffect, useState } from 'react';
import type { Room, Device } from '../types';
import { getDevicesInRoom, createGroup } from '../api/client';

interface Props {
  rooms: Room[];
  defaultRoomId?: string;
  onConfirm: () => Promise<void>;
  onCancel: () => void;
}

export function CreateGroupModal({ rooms, defaultRoomId, onConfirm, onCancel }: Props) {
  const [name, setName] = useState('');
  const [roomId, setRoomId] = useState(defaultRoomId ?? rooms[0]?.id ?? '');
  const [devices, setDevices] = useState<Device[]>([]);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [loadingDevices, setLoadingDevices] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!roomId) return;
    setLoadingDevices(true);
    setSelectedIds(new Set());
    getDevicesInRoom(roomId)
      .then((list) => {
        // Only show ungrouped devices
        setDevices(list.filter((d) => !d.groupId));
      })
      .catch(() => setDevices([]))
      .finally(() => setLoadingDevices(false));
  }, [roomId]);

  function toggleDevice(id: string) {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim() || !roomId) return;
    setSubmitting(true);
    setError(null);
    try {
      await createGroup({ name: name.trim(), roomId, deviceIds: Array.from(selectedIds) });
      await onConfirm();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create group');
      setSubmitting(false);
    }
  }

  const inputStyle: React.CSSProperties = {
    backgroundColor: 'var(--bg-hover)',
    border: '1px solid var(--border-default)',
    borderRadius: 'var(--radius-sm)',
    padding: '9px 13px',
    color: 'var(--text-primary)',
    outline: 'none',
    width: '100%',
    fontFamily: 'var(--font-body)',
    fontSize: 14,
    transition: 'border-color 0.2s, box-shadow 0.2s',
  };

  function handleFocus(e: React.FocusEvent<HTMLInputElement | HTMLSelectElement>) {
    e.target.style.borderColor = 'var(--accent-primary)';
    e.target.style.boxShadow = '0 0 0 3px var(--accent-primary-dim)';
  }

  function handleBlur(e: React.FocusEvent<HTMLInputElement | HTMLSelectElement>) {
    e.target.style.borderColor = 'var(--border-default)';
    e.target.style.boxShadow = 'none';
  }

  return (
    <div className="modal-overlay" onClick={(e) => e.target === e.currentTarget && onCancel()}>
      <div className="modal-dialog" style={{ width: 420 }}>
        <div className="modal-title">Create Group</div>

        <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <label className="form-label">Group Name</label>
            <input
              style={inputStyle}
              type="text"
              placeholder="e.g. Living Room Lights"
              value={name}
              onChange={(e) => setName(e.target.value)}
              onFocus={handleFocus}
              onBlur={handleBlur}
              autoFocus
            />
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <label className="form-label">Room</label>
            <select
              style={{ ...inputStyle, appearance: 'none' as const }}
              value={roomId}
              onChange={(e) => setRoomId(e.target.value)}
              onFocus={handleFocus}
              onBlur={handleBlur}
            >
              {rooms.map((r) => (
                <option key={r.id} value={r.id}>{r.name}</option>
              ))}
            </select>
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <label className="form-label">Member Devices</label>
            {loadingDevices ? (
              <div style={{ fontSize: 13, color: 'var(--text-muted)', padding: '8px 0' }}>Loading devices…</div>
            ) : devices.length === 0 ? (
              <div style={{ fontSize: 13, color: 'var(--text-muted)', padding: '8px 0' }}>No ungrouped devices in this room.</div>
            ) : (
              <div style={{
                border: '1px solid var(--border-default)',
                borderRadius: 'var(--radius-sm)',
                overflow: 'hidden',
                maxHeight: 180,
                overflowY: 'auto',
              }}>
                {devices.map((d, i) => (
                  <label
                    key={d.id}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: 10,
                      padding: '9px 13px',
                      cursor: 'pointer',
                      borderTop: i > 0 ? '1px solid var(--border-subtle)' : 'none',
                      background: selectedIds.has(d.id) ? 'var(--accent-primary-dim)' : 'var(--bg-hover)',
                      transition: 'background 0.15s',
                      fontSize: 14,
                    }}
                  >
                    <input
                      type="checkbox"
                      checked={selectedIds.has(d.id)}
                      onChange={() => toggleDevice(d.id)}
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

          {error && (
            <div style={{ color: 'var(--accent-red)', fontSize: 13 }}>{error}</div>
          )}

          <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end', marginTop: 4 }}>
            <button type="button" className="btn-secondary" onClick={onCancel}>
              Cancel
            </button>
            <button
              type="submit"
              className="btn-primary"
              style={{ opacity: submitting || !name.trim() ? 0.5 : 1 }}
              disabled={submitting || !name.trim() || !roomId}
            >
              {submitting ? 'Creating…' : 'Create Group'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
