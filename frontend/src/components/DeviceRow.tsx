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

  const controls: React.CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    gap: 12,
    flexShrink: 0,
    flexWrap: 'wrap',
    justifyContent: 'flex-end',
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
        <div key="dimmer" style={{ display: 'flex', alignItems: 'center', gap: 6, width: 110 }}>
          <input
            type="range"
            className="slider-dimmer"
            min={0}
            max={100}
            value={level}
            style={{
              background: `linear-gradient(to right, var(--accent-primary) ${level}%, var(--bg-hover) ${level}%)`,
            }}
            onChange={(e) => handleDimmer(Number(e.target.value))}
          />
        </div>
      );
    }

    if (device.capabilities.includes('Cover')) {
      const pos = typeof state['Cover'] === 'number' ? (state['Cover'] as number) : 0;
      items.push(
        <div key="cover" style={{ display: 'flex', alignItems: 'center', gap: 6, width: 110 }}>
          <input
            type="range"
            className="slider-cover"
            min={0}
            max={100}
            value={pos}
            style={{
              background: `linear-gradient(to right, var(--accent-teal) ${pos}%, var(--bg-hover) ${pos}%)`,
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
        <span key="temp" style={{ fontSize: 13, color: temp != null ? 'var(--accent-red)' : 'var(--text-muted)' }}>
          {temp != null ? `${Number(temp).toFixed(1)} °C` : '— °C'}
        </span>
      );
    }

    if (device.capabilities.includes('Power')) {
      const power = state['Power'];
      items.push(
        <span key="power" style={{ fontSize: 13, color: power != null ? 'var(--accent-blue)' : 'var(--text-muted)' }}>
          {power != null ? `${Number(power).toFixed(1)} W` : '— W'}
        </span>
      );
    }

    if (device.capabilities.includes('Energy')) {
      const energy = state['Energy'];
      items.push(
        <span key="energy" style={{ fontSize: 13, color: energy != null ? 'var(--accent-green)' : 'var(--text-muted)' }}>
          {energy != null ? `${Number(energy).toFixed(2)} kWh` : '— kWh'}
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
    <div className="device-row">
      <div style={{ flex: 1, minWidth: 0 }}>
        <Link to={`/devices/${device.id}`} className="device-name-link">
          {device.name}
        </Link>
        {roomName && (
          <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>{roomName}</div>
        )}
        {device.capabilities.length > 0 && (
          <div className="device-caps">
            {device.capabilities.join(' · ')}
          </div>
        )}
      </div>
      <div style={controls}>{renderControls()}</div>
    </div>
  );
}
