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
    await sendCommand(device.id, { capabilityKey: 'switch', value });
    onStateChange?.();
  }

  async function handleDimmer(value: number) {
    await sendCommand(device.id, { capabilityKey: 'dimmer', value });
    onStateChange?.();
  }

  async function handleCover(value: number) {
    await sendCommand(device.id, { capabilityKey: 'cover', value });
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

    if (device.capabilities.some(c => c.key === 'light')) {
      const lightState = state['light'] as Record<string, unknown> | undefined;
      const isOn = lightState?.on === true;
      items.push(
        <ToggleSwitch key="light" checked={isOn} onChange={v => sendCommand(device.id, { capabilityKey: 'light', value: v }).then(() => onStateChange?.())} />
      );
    }

    if (device.capabilities.some(c => c.key === 'switch') && !device.capabilities.some(c => c.key === 'light')) {
      const isOn = Boolean(state['switch']);
      items.push(
        <ToggleSwitch key="switch" checked={isOn} onChange={handleSwitch} />
      );
    }

    if (device.capabilities.some(c => c.key === 'dimmer') && !device.capabilities.some(c => c.key === 'light')) {
      const level = typeof state['dimmer'] === 'number' ? (state['dimmer'] as number) : 0;
      items.push(
        <div key="dimmer" style={{ width: 110 }}>
          <SliderControl value={level} className="slider-dimmer" accentColor="var(--accent-primary)" onCommit={handleDimmer} />
        </div>
      );
    }

    if (device.capabilities.some(c => c.key === 'cover')) {
      const pos = typeof state['cover'] === 'number' ? (state['cover'] as number) : 0;
      items.push(
        <div key="cover" style={{ width: 110 }}>
          <SliderControl value={pos} className="slider-cover" accentColor="var(--accent-teal)" onCommit={handleCover} />
        </div>
      );
    }

    if (device.capabilities.some(c => c.key === 'motion')) {
      const detected = Boolean(state['motion']);
      items.push(
        <StatusDot key="motion" active={detected} label={detected ? 'Detected' : 'Clear'} />
      );
    }

    if (device.capabilities.some(c => c.key === 'temperature')) {
      const temp = state['temperature'];
      items.push(
        <span key="temp" style={{ fontSize: 13, color: temp != null ? 'var(--accent-red)' : 'var(--text-muted)' }}>
          {temp != null ? `${Number(temp).toFixed(1)} °C` : '— °C'}
        </span>
      );
    }

    if (device.capabilities.some(c => c.key === 'power')) {
      const power = state['power'];
      items.push(
        <span key="power" style={{ fontSize: 13, color: power != null ? 'var(--accent-blue)' : 'var(--text-muted)' }}>
          {power != null ? `${Number(power).toFixed(1)} W` : '— W'}
        </span>
      );
    }

    if (device.capabilities.some(c => c.key === 'energy')) {
      const energy = state['energy'];
      items.push(
        <span key="energy" style={{ fontSize: 13, color: energy != null ? 'var(--accent-green)' : 'var(--text-muted)' }}>
          {energy != null ? `${Number(energy).toFixed(2)} kWh` : '— kWh'}
        </span>
      );
    }

    if (device.capabilities.some(c => c.key === 'humidity')) {
      const hum = typeof state['humidity'] === 'number' ? (state['humidity'] as number) : 0;
      items.push(
        <div key="hum" style={{ width: 80 }}>
          <ProgressBar value={hum} color="var(--accent-blue)" label={`${Math.round(hum)}%`} />
        </div>
      );
    }

    if (device.capabilities.some(c => c.key === 'contact')) {
      const closed = state['contact'] === true;
      items.push(
        <span key="contact" style={{ fontSize: 12, fontWeight: 500, color: closed ? 'var(--accent-teal)' : 'var(--accent-red)' }}>
          {closed ? 'Closed' : 'Open'}
        </span>
      );
    }

    if (device.capabilities.some(c => c.key === 'battery')) {
      const battery = state['battery'];
      if (battery != null) {
        const level = Number(battery);
        items.push(
          <span key="battery" style={{ fontSize: 11, color: level < 20 ? 'var(--accent-red)' : 'var(--text-muted)' }}>
            {Math.round(level)}%
          </span>
        );
      }
    }

    if (device.capabilities.some(c => c.key === 'presence')) {
      const present = state['presence'] === true;
      items.push(
        <span key="presence" style={{ fontSize: 12, fontWeight: 500, color: present ? 'var(--accent-green)' : 'var(--text-muted)' }}>
          {present ? 'Home' : 'Away'}
        </span>
      );
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
            {device.capabilities.map(c => c.label).join(' · ')}
          </div>
        )}
      </div>
      <div style={{ ...controls, opacity: isOffline ? 0.5 : 1 }}>{renderControls()}</div>
    </div>
  );
}
