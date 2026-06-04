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
    backgroundColor: 'var(--bg-card)',
    border: '1px solid var(--border)',
    borderRadius: 10,
    padding: 16,
    display: 'flex',
    flexDirection: 'column',
    gap: 10,
  };

  const header: React.CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingBottom: 8,
    borderBottom: '1px solid var(--border)',
  };

  const title: React.CSSProperties = {
    fontSize: 15,
    fontWeight: 600,
    color: 'var(--text-primary)',
  };

  const badge: React.CSSProperties = {
    fontSize: 11,
    fontWeight: 600,
    color: 'var(--text-muted)',
    backgroundColor: 'var(--border)',
    padding: '2px 8px',
    borderRadius: 10,
  };

  const empty: React.CSSProperties = {
    color: 'var(--text-dimmed)',
    fontSize: 13,
    textAlign: 'center',
    padding: '8px 0',
  };

  return (
    <div style={card}>
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
