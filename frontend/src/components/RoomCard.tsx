import React from 'react';
import type { Room, Device } from '../types';
import { DeviceRow } from './DeviceRow';

interface Props {
  room: Room;
  devices: Device[];
  onDeviceStateChange: () => void;
}

export function RoomCard({ room, devices, onDeviceStateChange }: Props) {
  const card: React.CSSProperties = {
    background: 'var(--bg-elevated)',
    border: '1px solid var(--border-subtle)',
    borderRadius: 'var(--radius-lg)',
    padding: '20px 22px',
    display: 'flex',
    flexDirection: 'column',
    boxShadow: 'var(--shadow-card)',
    transition: 'border-color 0.2s, box-shadow 0.2s',
    minWidth: 0,
  };

  const header: React.CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingBottom: 14,
    marginBottom: 4,
    borderBottom: '1px solid var(--border-subtle)',
  };

  const title: React.CSSProperties = {
    fontFamily: 'var(--font-heading)',
    fontSize: 16,
    fontWeight: 600,
    color: 'var(--text-primary)',
    letterSpacing: '-0.01em',
  };

  const badge: React.CSSProperties = {
    fontSize: 11,
    fontWeight: 600,
    color: 'var(--text-muted)',
    backgroundColor: 'var(--bg-hover)',
    border: '1px solid var(--border-subtle)',
    padding: '2px 9px',
    borderRadius: 20,
  };

  const empty: React.CSSProperties = {
    color: 'var(--text-muted)',
    fontSize: 13,
    textAlign: 'center',
    padding: '16px 0',
  };

  return (
    <div
      style={card}
      onMouseEnter={(e) => {
        (e.currentTarget as HTMLDivElement).style.borderColor = 'var(--border-hover)';
        (e.currentTarget as HTMLDivElement).style.boxShadow = 'var(--shadow-elevated)';
      }}
      onMouseLeave={(e) => {
        (e.currentTarget as HTMLDivElement).style.borderColor = 'var(--border-subtle)';
        (e.currentTarget as HTMLDivElement).style.boxShadow = 'var(--shadow-card)';
      }}
    >
      <div style={header}>
        <span style={title}>{room.name}</span>
        <span style={badge}>{devices.length} device{devices.length !== 1 ? 's' : ''}</span>
      </div>
      <div>
        {devices.length === 0 ? (
          <div style={empty}>No devices</div>
        ) : (
          devices.map((d) => (
            <DeviceRow
              key={d.id}
              device={d}
              onStateChange={onDeviceStateChange}
            />
          ))
        )}
      </div>
    </div>
  );
}
