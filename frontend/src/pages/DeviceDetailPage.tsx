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

  if (loading) {
    return <div style={{ color: 'var(--text-muted)', padding: 24, fontFamily: 'var(--font-body)' }}>Loading device…</div>;
  }

  if (error || !device) {
    return (
      <div className="page-content">
        <button
          style={{ display: 'inline-flex', alignItems: 'center', gap: 6, color: 'var(--text-muted)', fontSize: 13, cursor: 'pointer', marginBottom: 20, fontFamily: 'var(--font-body)' }}
          onClick={() => navigate(-1)}
        >
          ← Back
        </button>
        <div style={{ color: 'var(--accent-red)' }}>{error ?? 'Device not found'}</div>
      </div>
    );
  }

  const state = device.state ?? {};

  function capAccentColor(cap: string): string {
    switch (cap) {
      case 'Switch': return 'var(--accent-primary)';
      case 'Dimmer': return 'var(--accent-primary)';
      case 'Cover': return 'var(--accent-teal)';
      case 'Temperature': return 'var(--accent-red)';
      case 'Motion': return 'var(--accent-green)';
      case 'Power': return 'var(--accent-blue)';
      case 'Energy': return 'var(--accent-green)';
      case 'Humidity': return 'var(--accent-blue)';
      default: return 'var(--accent-primary)';
    }
  }

  function renderCapabilityCard(cap: string) {
    const accentColor = capAccentColor(cap);

    const cardStyle: React.CSSProperties = {
      background: 'var(--bg-elevated)',
      border: '1px solid var(--border-subtle)',
      borderRadius: 'var(--radius-md)',
      padding: '16px 18px',
      position: 'relative',
      overflow: 'hidden',
      boxShadow: 'var(--shadow-card)',
      transition: 'border-color 0.2s, box-shadow 0.2s',
    };

    const indicator: React.CSSProperties = {
      position: 'absolute',
      top: 0,
      left: 0,
      width: '100%',
      height: 2,
      background: accentColor,
      opacity: 0.7,
    };

    const labelStyle: React.CSSProperties = {
      fontSize: 10,
      fontWeight: 600,
      textTransform: 'uppercase' as const,
      letterSpacing: '0.08em',
      color: 'var(--text-muted)',
      marginBottom: 10,
    };

    const valueStyle = (color: string = 'var(--text-primary)'): React.CSSProperties => ({
      fontFamily: 'var(--font-heading)',
      fontSize: 22,
      fontWeight: 600,
      color,
      marginBottom: 2,
    });

    switch (cap) {
      case 'Switch': {
        const isOn = Boolean(state['Switch']);
        return (
          <div key={cap} style={cardStyle}>
            <div style={indicator} />
            <div style={labelStyle}>Switch</div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginTop: 6 }}>
              <ToggleSwitch checked={isOn} onChange={(v) => cmd('Switch', v)} />
              <span style={{
                fontSize: 14,
                fontWeight: 500,
                color: isOn ? 'var(--accent-primary)' : 'var(--text-muted)',
              }}>
                {isOn ? 'On' : 'Off'}
              </span>
            </div>
          </div>
        );
      }
      case 'Dimmer': {
        const level = typeof state['Dimmer'] === 'number' ? (state['Dimmer'] as number) : 0;
        return (
          <div key={cap} style={cardStyle}>
            <div style={indicator} />
            <div style={labelStyle}>Dimmer</div>
            <div style={valueStyle('var(--accent-primary)')}>{Math.round(level)}%</div>
            <div style={{ marginTop: 12 }}>
              <input
                type="range"
                className="slider-dimmer"
                min={0}
                max={100}
                value={level}
                style={{
                  width: '100%',
                  background: `linear-gradient(to right, var(--accent-primary) ${level}%, var(--bg-hover) ${level}%)`,
                }}
                onChange={(e) => cmd('Dimmer', Number(e.target.value))}
              />
            </div>
          </div>
        );
      }
      case 'Cover': {
        const pos = typeof state['Cover'] === 'number' ? (state['Cover'] as number) : 0;
        const btnStyle: React.CSSProperties = {
          flex: 1,
          padding: '10px 0',
          borderRadius: 'var(--radius-sm)',
          border: '1px solid var(--border-default)',
          background: 'var(--bg-hover)',
          color: 'var(--text-primary)',
          fontFamily: 'var(--font-body)',
          fontSize: 14,
          fontWeight: 600,
          cursor: 'pointer',
          transition: 'all 0.15s',
        };
        return (
          <div key={cap} style={cardStyle}>
            <div style={indicator} />
            <div style={labelStyle}>Cover</div>
            <div style={valueStyle('var(--accent-teal)')}>{Math.round(pos)}%</div>
            <div style={{ display: 'flex', gap: 8, marginTop: 14 }}>
              <button
                style={btnStyle}
                onMouseEnter={e => { e.currentTarget.style.background = 'var(--accent-teal-dim)'; e.currentTarget.style.borderColor = 'var(--accent-teal)'; }}
                onMouseLeave={e => { e.currentTarget.style.background = 'var(--bg-hover)'; e.currentTarget.style.borderColor = 'var(--border-default)'; }}
                onClick={() => cmd('Cover', 0)}
              >
                Close
              </button>
              <button
                style={btnStyle}
                onMouseEnter={e => { e.currentTarget.style.background = 'var(--accent-teal-dim)'; e.currentTarget.style.borderColor = 'var(--accent-teal)'; }}
                onMouseLeave={e => { e.currentTarget.style.background = 'var(--bg-hover)'; e.currentTarget.style.borderColor = 'var(--border-default)'; }}
                onClick={() => cmd('Cover', 50)}
              >
                50%
              </button>
              <button
                style={btnStyle}
                onMouseEnter={e => { e.currentTarget.style.background = 'var(--accent-teal-dim)'; e.currentTarget.style.borderColor = 'var(--accent-teal)'; }}
                onMouseLeave={e => { e.currentTarget.style.background = 'var(--bg-hover)'; e.currentTarget.style.borderColor = 'var(--border-default)'; }}
                onClick={() => cmd('Cover', 100)}
              >
                Open
              </button>
            </div>
            <div style={{ marginTop: 12 }}>
              <input
                type="range"
                className="slider-cover"
                min={0}
                max={100}
                value={pos}
                style={{
                  width: '100%',
                  background: `linear-gradient(to right, var(--accent-teal) ${pos}%, var(--bg-hover) ${pos}%)`,
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
          <div key={cap} style={cardStyle}>
            <div style={indicator} />
            <div style={labelStyle}>Temperature</div>
            <div style={valueStyle(temp != null ? 'var(--accent-red)' : 'var(--text-muted)')}>
              {temp != null ? `${Number(temp).toFixed(1)} °C` : '— °C'}
            </div>
          </div>
        );
      }
      case 'Motion': {
        const detected = Boolean(state['Motion']);
        return (
          <div key={cap} style={cardStyle}>
            <div style={indicator} />
            <div style={labelStyle}>Motion</div>
            <div style={{ marginTop: 8 }}>
              <StatusDot active={detected} label={detected ? 'Detected' : 'Clear'} />
            </div>
          </div>
        );
      }
      case 'Power': {
        const power = state['Power'];
        return (
          <div key={cap} style={cardStyle}>
            <div style={indicator} />
            <div style={labelStyle}>Power</div>
            <div style={valueStyle(power != null ? 'var(--accent-blue)' : 'var(--text-muted)')}>
              {power != null ? `${Number(power).toFixed(1)} W` : '— W'}
            </div>
          </div>
        );
      }
      case 'Energy': {
        const energy = state['Energy'];
        return (
          <div key={cap} style={cardStyle}>
            <div style={indicator} />
            <div style={labelStyle}>Energy</div>
            <div style={valueStyle(energy != null ? 'var(--accent-green)' : 'var(--text-muted)')}>
              {energy != null ? `${Number(energy).toFixed(2)} kWh` : '— kWh'}
            </div>
          </div>
        );
      }
      case 'Humidity': {
        const hum = typeof state['Humidity'] === 'number' ? (state['Humidity'] as number) : 0;
        return (
          <div key={cap} style={cardStyle}>
            <div style={indicator} />
            <div style={labelStyle}>Humidity</div>
            <div style={{ ...valueStyle('var(--accent-blue)'), marginBottom: 12 }}>
              {Math.round(hum)}%
            </div>
            <ProgressBar value={hum} color="var(--accent-blue)" />
          </div>
        );
      }
      default: {
        const val = state[cap];
        return (
          <div key={cap} style={cardStyle}>
            <div style={indicator} />
            <div style={labelStyle}>{cap}</div>
            <div style={valueStyle()}>
              {val != null ? String(val) : '—'}
            </div>
          </div>
        );
      }
    }
  }

  return (
    <div className="page-content">
      {/* Breadcrumb-style back */}
      <button
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: 6,
          color: 'var(--text-muted)',
          fontSize: 13,
          cursor: 'pointer',
          marginBottom: 22,
          fontFamily: 'var(--font-body)',
          transition: 'color 0.15s',
        }}
        onMouseEnter={(e) => (e.currentTarget.style.color = 'var(--accent-primary)')}
        onMouseLeave={(e) => (e.currentTarget.style.color = 'var(--text-muted)')}
        onClick={() => navigate(-1)}
      >
        ← All Devices
      </button>

      {/* Title row */}
      <div style={{ marginBottom: 28 }}>
        <div
          style={{
            fontFamily: 'var(--font-heading)',
            fontSize: 26,
            fontWeight: 700,
            color: 'var(--text-primary)',
            marginBottom: 6,
            letterSpacing: '-0.02em',
          }}
        >
          {device.name}
        </div>
        <div style={{ fontSize: 13, color: 'var(--text-muted)' }}>
          {device.communicationType}
          {device.capabilities.length > 0 && ' · '}
          {device.capabilities.join(' · ')}
        </div>
      </div>

      {/* Capability cards */}
      {device.capabilities.length > 0 && (
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
            gap: 14,
            marginBottom: 28,
          }}
        >
          {device.capabilities.map((cap) => renderCapabilityCard(cap))}
        </div>
      )}

      {/* Metadata */}
      {device.metadata && Object.keys(device.metadata).length > 0 && (
        <div
          style={{
            background: 'var(--bg-elevated)',
            border: '1px solid var(--border-subtle)',
            borderRadius: 'var(--radius-md)',
            padding: '16px 18px',
            boxShadow: 'var(--shadow-card)',
          }}
        >
          <div
            style={{
              fontSize: 10,
              fontWeight: 600,
              textTransform: 'uppercase',
              letterSpacing: '0.08em',
              color: 'var(--text-muted)',
              marginBottom: 12,
            }}
          >
            Metadata
          </div>
          {Object.entries(device.metadata).map(([k, v]) => (
            <div
              key={k}
              style={{
                display: 'flex',
                gap: 12,
                marginBottom: 8,
                fontSize: 13,
              }}
            >
              <span style={{ color: 'var(--text-muted)', minWidth: 120, flexShrink: 0 }}>{k}</span>
              <span style={{ color: 'var(--text-secondary)', wordBreak: 'break-word' }}>{v}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
