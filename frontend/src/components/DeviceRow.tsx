import React from 'react';
import { Link } from 'react-router-dom';
import type { Device } from '../types';
import { sendCommand } from '../api/client';
import { ToggleSwitch } from './ToggleSwitch';
import { ProgressBar } from './ProgressBar';
import { StatusDot } from './StatusDot';
import { SliderControl } from './SliderControl';
import { CapabilityIcon, primaryCapabilityIcon } from './CapabilityIcon';

interface Props {
  device: Device;
  showRoom?: boolean;
  rooms?: { id: string; name: string }[];
  onStateChange?: () => void;
  groupLabel?: string;
}

export function DeviceRow({ device, showRoom = false, rooms, onStateChange, groupLabel }: Props) {
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

    if (device.capabilities.includes('Light')) {
      const lightState = state['Light'] as Record<string, unknown> | undefined;
      const isOn = lightState?.on === true;
      items.push(
        <ToggleSwitch key="light" checked={isOn} onChange={v => sendCommand(device.id, { capability: 'Light', value: v }).then(() => onStateChange?.())} />
      );
    }

    if (device.capabilities.includes('Switch') && !device.capabilities.includes('Light')) {
      const isOn = Boolean(state['Switch']);
      items.push(
        <ToggleSwitch key="switch" checked={isOn} onChange={handleSwitch} />
      );
    }

    if (device.capabilities.includes('Dimmer') && !device.capabilities.includes('Light')) {
      const level = typeof state['Dimmer'] === 'number' ? (state['Dimmer'] as number) : 0;
      items.push(
        <div key="dimmer" style={{ width: 110 }}>
          <SliderControl value={level} className="slider-dimmer" accentColor="var(--accent-primary)" onCommit={handleDimmer} />
        </div>
      );
    }

    if (device.capabilities.includes('Cover')) {
      const pos = typeof state['Cover'] === 'number' ? (state['Cover'] as number) : 0;
      items.push(
        <div key="cover" style={{ width: 110 }}>
          <SliderControl value={pos} className="slider-cover" accentColor="var(--accent-teal)" onCommit={handleCover} />
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

    if (device.capabilities.includes('Contact')) {
      const closed = state['Contact'] === true;
      items.push(
        <span key="contact" style={{ fontSize: 12, fontWeight: 500, color: closed ? 'var(--accent-teal)' : 'var(--accent-red)' }}>
          {closed ? 'Closed' : 'Open'}
        </span>
      );
    }

    if (device.capabilities.includes('Battery')) {
      const battery = state['Battery'];
      if (battery != null) {
        const level = Number(battery);
        items.push(
          <span key="battery" style={{ fontSize: 11, color: level < 20 ? 'var(--accent-red)' : 'var(--text-muted)' }}>
            {Math.round(level)}%
          </span>
        );
      }
    }

    return items;
  }

  const isOffline = device.online === false;

  const primaryCap = primaryCapabilityIcon(device.capabilities);

  return (
    <div className="device-row">
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, opacity: isOffline ? 0.6 : 1 }}>
        <CapabilityIcon capability={primaryCap} size={20} color={isOffline ? 'var(--text-muted)' : undefined} />
      </div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <Link to={`/devices/${device.id}`} className="device-name-link">
            {device.name}
          </Link>
          {isOffline && (
            <span style={{ fontSize: 11, fontWeight: 600, color: 'var(--accent-red)', letterSpacing: '0.04em' }}>
              Offline
            </span>
          )}
        </div>
        {roomName && (
          <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>{roomName}</div>
        )}
        {groupLabel && (
          <div style={{ fontSize: 11, color: 'var(--accent-primary)', marginTop: 1, fontWeight: 500 }}>{groupLabel}</div>
        )}
        {device.capabilities.length > 0 && (
          <div className="device-caps">
            {device.capabilities.join(' · ')}
          </div>
        )}
      </div>
      <div style={{ ...controls, opacity: isOffline ? 0.5 : 1 }}>{renderControls()}</div>
    </div>
  );
}
