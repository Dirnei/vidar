import React from 'react';
import type { Device } from '../types';
import { ProgressBar } from './ProgressBar';
import { CapabilityIcon } from './CapabilityIcon';
import { EnumPicker } from './EnumPicker';

// One consolidated view of a Loxone room controller: the room's actual temperature next to the
// setpoint it's being driven toward, the operating mode driving it, and the valve position that
// mode produces — mirroring the app's other rich control cards (VacuumCard, BambuPrinterCard).

interface Props {
  device: Device;
  state: Record<string, unknown>;
  cmd: (capabilityKey: string, value: unknown) => void;
}

function num(v: unknown): number | null {
  return typeof v === 'number' ? v : null;
}

const cardStyle: React.CSSProperties = {
  position: 'relative',
  background: 'var(--bg-elevated)',
  border: '1px solid var(--border-default)',
  borderRadius: 'var(--radius-md)',
  padding: '20px 22px',
  boxShadow: 'var(--shadow-card)',
  overflow: 'hidden',
};

const sectionLabelStyle: React.CSSProperties = {
  fontSize: 11, fontWeight: 600, color: 'var(--text-muted)',
  textTransform: 'uppercase', letterSpacing: '0.07em', marginBottom: 8,
};

const dividerStyle: React.CSSProperties = { height: 1, background: 'var(--border-subtle)', margin: '18px 0' };

const stepBtnStyle: React.CSSProperties = {
  width: 38, height: 38, borderRadius: '50%', border: '1px solid var(--border-default)',
  background: 'var(--bg-hover)', color: 'var(--text-primary)', fontSize: 19, fontWeight: 600,
  cursor: 'pointer', display: 'flex', alignItems: 'center', justifyContent: 'center',
  fontFamily: 'var(--font-body)', transition: 'border-color 0.15s, color 0.15s', flexShrink: 0,
};

export function ClimateCard({ device, state, cmd }: Props) {
  const targetCap = device.capabilities.find(c => c.key === 'target_temp');
  if (!targetCap) return null;

  const modeCap = device.capabilities.find(c => c.key === 'climate_mode');
  const hasCurrentTemp = device.capabilities.some(c => c.key === 'temperature');
  const hasValve = device.capabilities.some(c => c.key === 'valve');

  const min = targetCap.min ?? 5;
  const max = targetCap.max ?? 30;
  const current = num(state['temperature']);
  const target = num(state['target_temp']) ?? min;
  const modeVal = num(state['climate_mode']);
  const valve = num(state['valve']);

  function step(delta: number) {
    const next = Math.round(Math.max(min, Math.min(max, target + delta)) * 2) / 2;
    if (next !== target) cmd('target_temp', next);
  }

  return (
    <div style={cardStyle}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <CapabilityIcon capability="temperature" size={15} />
        <span style={{ fontFamily: 'var(--font-heading)', fontSize: 15, fontWeight: 700, color: 'var(--text-primary)', letterSpacing: '-0.01em' }}>
          Climate
        </span>
      </div>

      <div style={{ display: 'flex', gap: 32, marginTop: 18, flexWrap: 'wrap', alignItems: 'flex-end' }}>
        {hasCurrentTemp && (
          <div>
            <div style={sectionLabelStyle}>Current</div>
            <div style={{
              fontFamily: 'var(--font-heading)', fontSize: 42, fontWeight: 700, lineHeight: 1,
              color: current != null ? 'var(--accent-red)' : 'var(--text-muted)',
            }}>
              {current != null ? current.toFixed(1) : '—'}
              <span style={{ fontSize: 18, fontWeight: 600 }}>°C</span>
            </div>
          </div>
        )}

        <div>
          <div style={sectionLabelStyle}>Target</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            <button
              type="button"
              style={stepBtnStyle}
              onClick={() => step(-0.5)}
              disabled={target <= min}
              aria-label="Decrease target temperature"
              onMouseEnter={e => { e.currentTarget.style.borderColor = 'var(--accent-primary)'; e.currentTarget.style.color = 'var(--accent-primary)'; }}
              onMouseLeave={e => { e.currentTarget.style.borderColor = 'var(--border-default)'; e.currentTarget.style.color = 'var(--text-primary)'; }}
            >
              −
            </button>
            <div style={{
              fontFamily: 'var(--font-heading)', fontSize: 42, fontWeight: 700, lineHeight: 1,
              color: 'var(--accent-primary)', minWidth: 96, textAlign: 'center',
            }}>
              {target.toFixed(1)}
              <span style={{ fontSize: 18, fontWeight: 600 }}>°C</span>
            </div>
            <button
              type="button"
              style={stepBtnStyle}
              onClick={() => step(0.5)}
              disabled={target >= max}
              aria-label="Increase target temperature"
              onMouseEnter={e => { e.currentTarget.style.borderColor = 'var(--accent-primary)'; e.currentTarget.style.color = 'var(--accent-primary)'; }}
              onMouseLeave={e => { e.currentTarget.style.borderColor = 'var(--border-default)'; e.currentTarget.style.color = 'var(--text-primary)'; }}
            >
              +
            </button>
          </div>
        </div>
      </div>

      {(modeCap || hasValve) && <div style={dividerStyle} />}

      {(modeCap || hasValve) && (
        <div style={{ display: 'flex', gap: 28, flexWrap: 'wrap' }}>
          {modeCap && modeCap.options && modeCap.options.length > 0 && (
            <div style={{ flex: '1 1 180px', minWidth: 160 }}>
              <div style={sectionLabelStyle}>Mode</div>
              <EnumPicker
                options={modeCap.options}
                value={modeVal ?? undefined}
                accent="var(--accent-primary)"
                onChange={v => cmd('climate_mode', v)}
              />
            </div>
          )}
          {hasValve && (
            <div style={{ flex: '1 1 180px', minWidth: 160 }}>
              <div style={sectionLabelStyle}>Valve</div>
              <div style={{
                fontFamily: 'var(--font-heading)', fontSize: 20, fontWeight: 700, marginBottom: 8,
                color: valve != null ? 'var(--accent-teal)' : 'var(--text-muted)',
              }}>
                {valve != null ? `${Math.round(valve)}%` : '—'}
              </div>
              {valve != null && <ProgressBar value={valve} color="var(--accent-teal)" />}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
