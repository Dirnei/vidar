import React from 'react';
import { Link } from 'react-router-dom';
import type { DeviceGroup } from '../types';
import { sendGroupCommand } from '../api/client';
import { ToggleSwitch } from './ToggleSwitch';
import { SliderControl } from './SliderControl';
import { StatusDot } from './StatusDot';
import { ProgressBar } from './ProgressBar';

interface Props {
  group: DeviceGroup;
  onStateChange?: () => void;
}

function GroupIcon() {
  return (
    <svg width={20} height={20} viewBox="0 0 24 24" fill="none" stroke="var(--accent-primary)" strokeWidth="2">
      <rect x="3" y="3" width="14" height="14" rx="2" />
      <rect x="7" y="7" width="14" height="14" rx="2" />
    </svg>
  );
}

export const GroupRow = React.memo(function GroupRow({ group, onStateChange }: Props) {
  const state = group.state ?? {};
  const capabilities = group.capabilities ?? [];

  async function handleGroupCommand(capabilityKey: string, value: unknown) {
    await sendGroupCommand(group.id, { capabilityKey, value });
    onStateChange?.();
  }

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

    if (capabilities.includes('light')) {
      const lightState = state['light'] as Record<string, unknown> | undefined;
      const isOn = lightState?.on === true;
      items.push(
        <ToggleSwitch
          key="light"
          checked={isOn}
          onChange={v => handleGroupCommand('light', v)}
        />
      );
    }

    if (capabilities.includes('switch') && !capabilities.includes('light')) {
      const isOn = Boolean(state['switch']);
      items.push(
        <ToggleSwitch
          key="switch"
          checked={isOn}
          onChange={v => handleGroupCommand('switch', v)}
        />
      );
    }

    if (capabilities.includes('dimmer') && !capabilities.includes('light')) {
      const level = typeof state['dimmer'] === 'number' ? (state['dimmer'] as number) : 0;
      items.push(
        <div key="dimmer" style={{ width: 110 }}>
          <SliderControl
            value={level}
            className="slider-dimmer"
            accentColor="var(--accent-primary)"
            onCommit={v => handleGroupCommand('dimmer', v)}
          />
        </div>
      );
    }

    if (capabilities.includes('cover')) {
      const pos = typeof state['cover'] === 'number' ? (state['cover'] as number) : 0;
      items.push(
        <div key="cover" style={{ width: 110 }}>
          <SliderControl
            value={pos}
            className="slider-cover"
            accentColor="var(--accent-teal)"
            onCommit={v => handleGroupCommand('cover', v)}
          />
        </div>
      );
    }

    if (capabilities.includes('motion')) {
      const detected = Boolean(state['motion']);
      items.push(
        <StatusDot key="motion" active={detected} label={detected ? 'Detected' : 'Clear'} />
      );
    }

    if (capabilities.includes('temperature')) {
      const temp = state['temperature'];
      items.push(
        <span key="temp" style={{ fontSize: 13, color: temp != null ? 'var(--accent-red)' : 'var(--text-muted)' }}>
          {temp != null ? `${Number(temp).toFixed(1)} °C` : '— °C'}
        </span>
      );
    }

    if (capabilities.includes('power')) {
      const power = state['power'];
      items.push(
        <span key="power" style={{ fontSize: 13, color: power != null ? 'var(--accent-blue)' : 'var(--text-muted)' }}>
          {power != null ? `${Number(power).toFixed(1)} W` : '— W'}
        </span>
      );
    }

    if (capabilities.includes('energy')) {
      const energy = state['energy'];
      items.push(
        <span key="energy" style={{ fontSize: 13, color: energy != null ? 'var(--accent-green)' : 'var(--text-muted)' }}>
          {energy != null ? `${Number(energy).toFixed(2)} kWh` : '— kWh'}
        </span>
      );
    }

    if (capabilities.includes('humidity')) {
      const hum = typeof state['humidity'] === 'number' ? (state['humidity'] as number) : 0;
      items.push(
        <div key="hum" style={{ width: 80 }}>
          <ProgressBar value={hum} color="var(--accent-blue)" label={`${Math.round(hum)}%`} />
        </div>
      );
    }

    return items;
  }

  const isOffline = group.online === false;

  return (
    <div className="device-row">
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, opacity: isOffline ? 0.6 : 1 }}>
        <GroupIcon />
      </div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <Link to={`/groups/${group.id}`} className="device-name-link">
            {group.name}
          </Link>
          {isOffline && (
            <span style={{ fontSize: 11, fontWeight: 600, color: 'var(--accent-red)', letterSpacing: '0.04em' }}>
              Offline
            </span>
          )}
        </div>
        <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>
          {group.deviceIds.length} device{group.deviceIds.length !== 1 ? 's' : ''}
        </div>
        {capabilities.length > 0 && (
          <div className="device-caps">
            {capabilities.join(' · ')}
          </div>
        )}
      </div>
      <div style={{ ...controls, opacity: isOffline ? 0.5 : 1 }}>{renderControls()}</div>
    </div>
  );
});
