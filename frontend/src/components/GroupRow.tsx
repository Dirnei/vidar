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

export function GroupRow({ group, onStateChange }: Props) {
  const state = group.state ?? {};
  const capabilities = group.capabilities ?? [];

  async function handleGroupCommand(capability: string, value: unknown) {
    await sendGroupCommand(group.id, { capability, value });
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

    if (capabilities.includes('Light')) {
      const lightState = state['Light'] as Record<string, unknown> | undefined;
      const isOn = lightState?.on === true;
      items.push(
        <ToggleSwitch
          key="light"
          checked={isOn}
          onChange={v => handleGroupCommand('Light', v)}
        />
      );
    }

    if (capabilities.includes('Switch') && !capabilities.includes('Light')) {
      const isOn = Boolean(state['Switch']);
      items.push(
        <ToggleSwitch
          key="switch"
          checked={isOn}
          onChange={v => handleGroupCommand('Switch', v)}
        />
      );
    }

    if (capabilities.includes('Dimmer') && !capabilities.includes('Light')) {
      const level = typeof state['Dimmer'] === 'number' ? (state['Dimmer'] as number) : 0;
      items.push(
        <div key="dimmer" style={{ width: 110 }}>
          <SliderControl
            value={level}
            className="slider-dimmer"
            accentColor="var(--accent-primary)"
            onCommit={v => handleGroupCommand('Dimmer', v)}
          />
        </div>
      );
    }

    if (capabilities.includes('Cover')) {
      const pos = typeof state['Cover'] === 'number' ? (state['Cover'] as number) : 0;
      items.push(
        <div key="cover" style={{ width: 110 }}>
          <SliderControl
            value={pos}
            className="slider-cover"
            accentColor="var(--accent-teal)"
            onCommit={v => handleGroupCommand('Cover', v)}
          />
        </div>
      );
    }

    if (capabilities.includes('Motion')) {
      const detected = Boolean(state['Motion']);
      items.push(
        <StatusDot key="motion" active={detected} label={detected ? 'Detected' : 'Clear'} />
      );
    }

    if (capabilities.includes('Temperature')) {
      const temp = state['Temperature'];
      items.push(
        <span key="temp" style={{ fontSize: 13, color: temp != null ? 'var(--accent-red)' : 'var(--text-muted)' }}>
          {temp != null ? `${Number(temp).toFixed(1)} °C` : '— °C'}
        </span>
      );
    }

    if (capabilities.includes('Power')) {
      const power = state['Power'];
      items.push(
        <span key="power" style={{ fontSize: 13, color: power != null ? 'var(--accent-blue)' : 'var(--text-muted)' }}>
          {power != null ? `${Number(power).toFixed(1)} W` : '— W'}
        </span>
      );
    }

    if (capabilities.includes('Energy')) {
      const energy = state['Energy'];
      items.push(
        <span key="energy" style={{ fontSize: 13, color: energy != null ? 'var(--accent-green)' : 'var(--text-muted)' }}>
          {energy != null ? `${Number(energy).toFixed(2)} kWh` : '— kWh'}
        </span>
      );
    }

    if (capabilities.includes('Humidity')) {
      const hum = typeof state['Humidity'] === 'number' ? (state['Humidity'] as number) : 0;
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
}
