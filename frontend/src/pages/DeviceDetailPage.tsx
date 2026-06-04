import React, { useCallback, useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import type { Device } from '../types';
import { getDevice, sendCommand } from '../api/client';
import { subscribeDeviceState } from '../api/sse';
import { ToggleSwitch } from '../components/ToggleSwitch';
import { ProgressBar } from '../components/ProgressBar';
import { StatusDot } from '../components/StatusDot';

export function DeviceDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [device, setDevice] = useState<Device | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadDevice = useCallback(async () => {
    if (!id) return;
    try {
      const d = await getDevice(id);
      setDevice(d);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load device');
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    loadDevice();
    const unsub = subscribeDeviceState((evt) => {
      if (evt.deviceId === id) loadDevice();
    });
    return unsub;
  }, [loadDevice, id]);

  async function cmd(capability: string, value: unknown) {
    if (!id) return;
    await sendCommand(id, { capability, value });
    await loadDevice();
  }

  const backBtn: React.CSSProperties = {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 6,
    color: 'var(--text-muted)',
    fontSize: 13,
    cursor: 'pointer',
    marginBottom: 20,
    padding: '4px 0',
  };

  const titleRow: React.CSSProperties = {
    marginBottom: 24,
  };

  const titleStyle: React.CSSProperties = {
    fontSize: 22,
    fontWeight: 700,
    color: 'var(--text-primary)',
    marginBottom: 4,
  };

  const subtitleStyle: React.CSSProperties = {
    fontSize: 13,
    color: 'var(--text-muted)',
  };

  const capGrid: React.CSSProperties = {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))',
    gap: 14,
    marginBottom: 24,
  };

  const capCard: React.CSSProperties = {
    backgroundColor: 'var(--bg-card)',
    border: '1px solid var(--border)',
    borderRadius: 8,
    padding: '14px 16px',
  };

  const capLabel: React.CSSProperties = {
    fontSize: 11,
    fontWeight: 600,
    textTransform: 'uppercase' as const,
    letterSpacing: '0.06em',
    color: 'var(--text-dimmed)',
    marginBottom: 10,
  };

  const valueText = (color: string = 'var(--text-primary)'): React.CSSProperties => ({
    fontSize: 20,
    fontWeight: 600,
    color,
  });

  const metaSection: React.CSSProperties = {
    backgroundColor: 'var(--bg-card)',
    border: '1px solid var(--border)',
    borderRadius: 8,
    padding: '14px 16px',
  };

  const metaTitle: React.CSSProperties = {
    fontSize: 12,
    fontWeight: 600,
    textTransform: 'uppercase',
    letterSpacing: '0.06em',
    color: 'var(--text-dimmed)',
    marginBottom: 10,
  };

  const metaRow: React.CSSProperties = {
    display: 'flex',
    gap: 12,
    marginBottom: 6,
    fontSize: 13,
  };

  const metaKey: React.CSSProperties = {
    color: 'var(--text-muted)',
    minWidth: 120,
    flexShrink: 0,
  };

  const metaVal: React.CSSProperties = {
    color: 'var(--text-secondary)',
    wordBreak: 'break-word',
  };

  if (loading) {
    return <div style={{ color: 'var(--text-muted)', padding: 24 }}>Loading device…</div>;
  }

  if (error || !device) {
    return (
      <div>
        <button style={backBtn} onClick={() => navigate(-1)}>← Back</button>
        <div style={{ color: 'var(--accent-red)' }}>{error ?? 'Device not found'}</div>
      </div>
    );
  }

  const state = device.state ?? {};

  function renderCapabilityCard(cap: string) {
    switch (cap) {
      case 'Switch': {
        const isOn = Boolean(state['Switch']);
        return (
          <div key={cap} style={capCard}>
            <div style={capLabel}>Switch</div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
              <ToggleSwitch checked={isOn} onChange={(v) => cmd('Switch', v)} />
              <span style={{ fontSize: 14, color: isOn ? 'var(--accent-green)' : 'var(--text-dimmed)' }}>
                {isOn ? 'On' : 'Off'}
              </span>
            </div>
          </div>
        );
      }
      case 'Dimmer': {
        const level = typeof state['Dimmer'] === 'number' ? (state['Dimmer'] as number) : 0;
        return (
          <div key={cap} style={capCard}>
            <div style={capLabel}>Dimmer</div>
            <div style={valueText('var(--accent-yellow)')}>{Math.round(level)}%</div>
            <div style={{ marginTop: 10 }}>
              <input
                type="range"
                min={0}
                max={100}
                value={level}
                style={{
                  width: '100%',
                  background: `linear-gradient(to right, var(--accent-yellow) ${level}%, var(--border) ${level}%)`,
                  accentColor: 'var(--accent-yellow)',
                }}
                onChange={(e) => cmd('Dimmer', Number(e.target.value))}
              />
            </div>
          </div>
        );
      }
      case 'Cover': {
        const pos = typeof state['Cover'] === 'number' ? (state['Cover'] as number) : 0;
        return (
          <div key={cap} style={capCard}>
            <div style={capLabel}>Cover</div>
            <div style={valueText('var(--accent-blue)')}>{Math.round(pos)}%</div>
            <div style={{ marginTop: 10 }}>
              <input
                type="range"
                min={0}
                max={100}
                value={pos}
                style={{
                  width: '100%',
                  background: `linear-gradient(to right, var(--accent-blue) ${pos}%, var(--border) ${pos}%)`,
                  accentColor: 'var(--accent-blue)',
                }}
                onChange={(e) => cmd('Cover', Number(e.target.value))}
              />
            </div>
          </div>
        );
      }
      case 'Temperature': {
        const temp = state['Temperature'];
        return (
          <div key={cap} style={capCard}>
            <div style={capLabel}>Temperature</div>
            <div style={valueText('var(--accent-red)')}>
              {temp != null ? `${Number(temp).toFixed(1)}°C` : '—'}
            </div>
          </div>
        );
      }
      case 'Motion': {
        const detected = Boolean(state['Motion']);
        return (
          <div key={cap} style={capCard}>
            <div style={capLabel}>Motion</div>
            <div style={{ marginTop: 4 }}>
              <StatusDot active={detected} label={detected ? 'Detected' : 'Clear'} />
            </div>
          </div>
        );
      }
      case 'Power': {
        const power = state['Power'];
        return (
          <div key={cap} style={capCard}>
            <div style={capLabel}>Power</div>
            <div style={valueText()}>
              {power != null ? `${Number(power).toFixed(1)} W` : '—'}
            </div>
          </div>
        );
      }
      case 'Energy': {
        const energy = state['Energy'];
        return (
          <div key={cap} style={capCard}>
            <div style={capLabel}>Energy</div>
            <div style={valueText()}>
              {energy != null ? `${Number(energy).toFixed(2)} kWh` : '—'}
            </div>
          </div>
        );
      }
      case 'Humidity': {
        const hum = typeof state['Humidity'] === 'number' ? (state['Humidity'] as number) : 0;
        return (
          <div key={cap} style={capCard}>
            <div style={capLabel}>Humidity</div>
            <div style={{ ...valueText('var(--accent-blue)'), marginBottom: 10 }}>
              {Math.round(hum)}%
            </div>
            <ProgressBar value={hum} color="var(--accent-blue)" label={`${Math.round(hum)}%`} />
          </div>
        );
      }
      default: {
        const val = state[cap];
        return (
          <div key={cap} style={capCard}>
            <div style={capLabel}>{cap}</div>
            <div style={valueText()}>
              {val != null ? String(val) : '—'}
            </div>
          </div>
        );
      }
    }
  }

  return (
    <div>
      <button style={backBtn} onClick={() => navigate(-1)}>
        ← Back
      </button>

      <div style={titleRow}>
        <div style={titleStyle}>{device.name}</div>
        <div style={subtitleStyle}>
          {device.communicationType}
          {device.roomId && ' · '}
          {device.capabilities.join(' · ')}
        </div>
      </div>

      {device.capabilities.length > 0 && (
        <div style={capGrid}>
          {device.capabilities.map((cap) => renderCapabilityCard(cap))}
        </div>
      )}

      {device.metadata && Object.keys(device.metadata).length > 0 && (
        <div style={metaSection}>
          <div style={metaTitle}>Metadata</div>
          {Object.entries(device.metadata).map(([k, v]) => (
            <div key={k} style={metaRow}>
              <span style={metaKey}>{k}</span>
              <span style={metaVal}>{v}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
