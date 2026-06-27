import React, { useState } from 'react';
import type { Room } from '../types';

interface Props {
  rooms: Room[];
  defaultName?: string;
  defaultRoomId?: string;
  showIpField?: boolean;
  onConfirm: (name: string, roomId: string, ip?: string) => Promise<void>;
  onCancel: () => void;
}

export function ConfigureDeviceModal({ rooms, defaultName, defaultRoomId, showIpField, onConfirm, onCancel }: Props) {
  const [name, setName] = useState(defaultName ?? '');
  const [roomId, setRoomId] = useState(defaultRoomId ?? rooms[0]?.id ?? '');
  const [ip, setIp] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

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

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim() || !roomId) return;
    setSubmitting(true);
    setError(null);
    try {
      await onConfirm(name.trim(), roomId, ip.trim() || undefined);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to configure device');
      setSubmitting(false);
    }
  }

  return (
    <div className="modal-overlay" onClick={(e) => e.target === e.currentTarget && onCancel()}>
      <div className="modal-dialog">
        <div className="modal-title">Configure Device</div>

        <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <label className="form-label">Device Name</label>
            <input
              style={inputStyle}
              type="text"
              placeholder="e.g. Living Room Light"
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

          {showIpField && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <label className="form-label">
                Local IP
                <span style={{
                  fontWeight: 400,
                  textTransform: 'none',
                  letterSpacing: 0,
                  fontSize: 11,
                  color: 'var(--text-muted)',
                  marginLeft: 6,
                }}>
                  optional
                </span>
              </label>
              <input
                style={inputStyle}
                type="text"
                placeholder="192.168.1.x"
                value={ip}
                onChange={(e) => setIp(e.target.value)}
                onFocus={handleFocus}
                onBlur={handleBlur}
                autoComplete="off"
              />
              <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: -2 }}>
                Used for direct LAN control — leave blank to skip
              </div>
            </div>
          )}

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
              {submitting ? 'Saving…' : 'Configure'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
