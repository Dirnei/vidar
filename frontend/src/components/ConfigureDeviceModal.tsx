import React, { useState } from 'react';
import type { Room } from '../types';

interface Props {
  rooms: Room[];
  defaultName?: string;
  onConfirm: (name: string, roomId: string) => Promise<void>;
  onCancel: () => void;
}

export function ConfigureDeviceModal({ rooms, defaultName, onConfirm, onCancel }: Props) {
  const [name, setName] = useState(defaultName ?? '');
  const [roomId, setRoomId] = useState(rooms[0]?.id ?? '');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const overlay: React.CSSProperties = {
    position: 'fixed',
    inset: 0,
    backgroundColor: 'rgba(0,0,0,0.6)',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    zIndex: 1000,
  };

  const dialog: React.CSSProperties = {
    backgroundColor: 'var(--bg-card)',
    border: '1px solid var(--border)',
    borderRadius: 10,
    padding: 24,
    width: 360,
    display: 'flex',
    flexDirection: 'column',
    gap: 16,
  };

  const titleStyle: React.CSSProperties = {
    fontSize: 16,
    fontWeight: 600,
    color: 'var(--text-primary)',
  };

  const fieldStyle: React.CSSProperties = {
    display: 'flex',
    flexDirection: 'column',
    gap: 6,
  };

  const labelStyle: React.CSSProperties = {
    fontSize: 12,
    color: 'var(--text-muted)',
    fontWeight: 500,
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
  };

  const inputStyle: React.CSSProperties = {
    backgroundColor: 'var(--bg-row)',
    border: '1px solid var(--border)',
    borderRadius: 6,
    padding: '8px 12px',
    color: 'var(--text-primary)',
    outline: 'none',
    width: '100%',
  };

  const actions: React.CSSProperties = {
    display: 'flex',
    gap: 10,
    justifyContent: 'flex-end',
  };

  const btnBase: React.CSSProperties = {
    padding: '8px 18px',
    borderRadius: 6,
    fontWeight: 500,
    fontSize: 13,
    cursor: 'pointer',
  };

  const btnCancel: React.CSSProperties = {
    ...btnBase,
    backgroundColor: 'var(--bg-row)',
    border: '1px solid var(--border)',
    color: 'var(--text-secondary)',
  };

  const btnConfirm: React.CSSProperties = {
    ...btnBase,
    backgroundColor: 'var(--tab-active)',
    border: '1px solid var(--tab-active)',
    color: '#fff',
    opacity: submitting || !name.trim() ? 0.6 : 1,
  };

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim() || !roomId) return;
    setSubmitting(true);
    setError(null);
    try {
      await onConfirm(name.trim(), roomId);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to configure device');
      setSubmitting(false);
    }
  }

  return (
    <div style={overlay} onClick={(e) => e.target === e.currentTarget && onCancel()}>
      <div style={dialog}>
        <div style={titleStyle}>Configure Device</div>

        <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <div style={fieldStyle}>
            <label style={labelStyle}>Device Name</label>
            <input
              style={inputStyle}
              type="text"
              placeholder="e.g. Living Room Light"
              value={name}
              onChange={(e) => setName(e.target.value)}
              autoFocus
            />
          </div>

          <div style={fieldStyle}>
            <label style={labelStyle}>Room</label>
            <select
              style={{ ...inputStyle, appearance: 'none' }}
              value={roomId}
              onChange={(e) => setRoomId(e.target.value)}
            >
              {rooms.map((r) => (
                <option key={r.id} value={r.id}>{r.name}</option>
              ))}
            </select>
          </div>

          {error && (
            <div style={{ color: 'var(--accent-red)', fontSize: 13 }}>{error}</div>
          )}

          <div style={actions}>
            <button type="button" style={btnCancel} onClick={onCancel}>
              Cancel
            </button>
            <button
              type="submit"
              style={btnConfirm}
              disabled={submitting || !name.trim() || !roomId}
            >
              {submitting ? 'Saving…' : 'Configure'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
