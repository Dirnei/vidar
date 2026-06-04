import React from 'react';
import { Link } from 'react-router-dom';
import type { Device } from '../types';
import { sendCommand } from '../api/client';
import { ToggleSwitch } from './ToggleSwitch';
import { ProgressBar } from './ProgressBar';
import { StatusDot } from './StatusDot';

interface Props {
  device: Device;
  showRoom?: boolean;
  rooms?: { id: string; name: string }[];
  onStateChange?: () => void;
}

export function DeviceRow({ device, showRoom = false, rooms, onStateChange }: Props) {
  const state = device.state ?? {};

  async function handleSwitch(value: boolean) {
    await sendCommand(device.id, { capability: 'Switch', value });
    onStateChange?.();
  }

  async function handleDimmer(value: number) {
    await sendCommand(device.id, { capability: 'Dimmer', value });
    onStateChange?.();
  }

  async function handleCover(value: number) {
    await sendCommand(device.id, { capability: 'Cover', value });
    onStateChange?.();
  }

  const roomName = showRoom && rooms
    ? (rooms.find((r) => r.id === device.roomId)?.name ?? 'Unassigned')
    : null;

  const row: React.CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    gap: 12,
    padding: '10px 14px',
    backgroundColor: 'var(--bg-row)',
    borderRadius: 6,
    marginBottom: 4,
  };

  const nameSection: React.CSSProperties = {
    flex: 1,
    minWidth: 0,
  };

  const nameLink: React.CSSProperties = {
    fontWeight: 500,
    color: 'var(--text-primary)',
    display: 'block',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  };

  const sub: React.CSSProperties = {
    fontSize: 12,
    color: 'var(--text-muted)',
    marginTop: 2,
  };

  const controls: React.CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    gap: 10,
    flexShrink: 0,
    maxWidth: 200,
  };

  function renderControls() {
    const items: React.ReactNode[] = [];

    if (device.capabilities.includes('Switch')) {
      const isOn = Boolean(state['Switch']);
      items.push(
        <ToggleSwitch key="switch" checked={isOn} onChange={handleSwitch} />
      );
    }

    if (device.capabilities.includes('Dimmer')) {
      const level = typeof state['Dimmer'] === 'number' ? (state['Dimmer'] as number) : 0;
      items.push(
        <div key="dimmer" style={{ display: 'flex', alignItems: 'center', gap: 6, width: 100 }}>
          <input
            type="range"
            min={0}
            max={100}
            value={level}
            style={{
              background: `linear-gradient(to right, var(--accent-yellow) ${level}%, var(--border) ${level}%)`,
              accentColor: 'var(--accent-yellow)',
            }}
            onChange={(e) => handleDimmer(Number(e.target.value))}
          />
        </div>
      );
    }

    if (device.capabilities.includes('Cover')) {
      const pos = typeof state['Cover'] === 'number' ? (state['Cover'] as number) : 0;
      items.push(
        <div key="cover" style={{ display: 'flex', alignItems: 'center', gap: 6, width: 100 }}>
          <input
            type="range"
            min={0}
            max={100}
            value={pos}
            style={{
              background: `linear-gradient(to right, var(--accent-blue) ${pos}%, var(--border) ${pos}%)`,
              accentColor: 'var(--accent-blue)',
            }}
            onChange={(e) => handleCover(Number(e.target.value))}
          />
        </div>
      );
    }

    if (device.capabilities.includes('Motion')) {
      const detected = Boolean(state['Motion']);
      items.push(
        <StatusDot key="motion" active={detected} label={detected ? 'Detected' : 'Clear'} />
      );
    }

    if (device.capabilities.includes('Temperature')) {
      const temp = state['Temperature'];
      items.push(
        <span key="temp" style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
          {temp != null ? `${Number(temp).toFixed(1)}°C` : '—'}
        </span>
      );
    }

    if (device.capabilities.includes('Power')) {
      const power = state['Power'];
      items.push(
        <span key="power" style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
          {power != null ? `${Number(power).toFixed(1)} W` : '—'}
        </span>
      );
    }

    if (device.capabilities.includes('Energy')) {
      const energy = state['Energy'];
      items.push(
        <span key="energy" style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
          {energy != null ? `${Number(energy).toFixed(2)} kWh` : '—'}
        </span>
      );
    }

    if (device.capabilities.includes('Humidity')) {
      const hum = typeof state['Humidity'] === 'number' ? (state['Humidity'] as number) : 0;
      items.push(
        <div key="hum" style={{ width: 80 }}>
          <ProgressBar value={hum} color="var(--accent-blue)" label={`${Math.round(hum)}%`} />
        </div>
      );
    }

    return items;
  }

  return (
    <div style={row}>
      <div style={nameSection}>
        <Link to={`/devices/${device.id}`} style={nameLink}>
          {device.name}
        </Link>
        {roomName && <div style={sub}>{roomName}</div>}
        {device.capabilities.length > 0 && (
          <div style={{ ...sub, fontSize: 11, color: 'var(--text-dimmed)' }}>
            {device.capabilities.join(' · ')}
          </div>
        )}
      </div>
      <div style={controls}>{renderControls()}</div>
    </div>
  );
}
