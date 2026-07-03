import React, { useState } from 'react';
import type { Device } from '../types';
import { SliderControl } from './SliderControl';

// One consolidated control surface for a robot vacuum — status, actions, suction, clean-by-room,
// and Roborock routines — in a single card, mirroring the app's other rich control cards.

interface Props {
  device: Device;
  state: Record<string, unknown>;
  cmd: (capabilityKey: string, value: unknown) => void;
}

interface NamedItem { id: number; name: string; }

// Roborock fan-power codes → friendly suction names (unknown codes show the raw level).
const SUCTION_LABELS: Record<number, string> = {
  101: 'Quiet', 102: 'Balanced', 103: 'Turbo', 104: 'Max', 105: 'Off', 106: 'Max+',
};

function stateColor(s: string): string {
  switch (s) {
    case 'cleaning': return 'var(--accent-green)';
    case 'returning': return 'var(--accent-blue)';
    case 'paused': return 'var(--accent-yellow)';
    case 'error': return 'var(--accent-red)';
    case 'docked': return 'var(--accent-primary)';
    default: return 'var(--text-muted)';
  }
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

const dividerStyle: React.CSSProperties = {
  height: 1, background: 'var(--border-subtle)', margin: '16px 0',
};

function actionButton(label: string, onClick: () => void, primary = false) {
  return (
    <button
      key={label}
      type="button"
      className={primary ? 'btn-primary' : 'btn-secondary'}
      style={{ flex: '1 1 auto', minWidth: 84 }}
      onClick={onClick}
    >
      {label}
    </button>
  );
}

export function VacuumCard({ device, state, cmd }: Props) {
  const [selected, setSelected] = useState<Set<number>>(new Set());

  const vstate = String(state['vacuum.state'] ?? 'unknown');
  const battery = state['vacuum.battery'];
  const rooms = (state['vacuum.rooms'] as NamedItem[] | undefined) ?? [];
  const scenes = (state['vacuum.scenes'] as NamedItem[] | undefined) ?? [];

  const fanCap = device.capabilities.find(c => c.key === 'vacuum.fanPower');
  const fanMin = fanCap?.min ?? 101;
  const fanMax = fanCap?.max ?? 106;
  const fan = typeof state['vacuum.fanPower'] === 'number' ? (state['vacuum.fanPower'] as number) : fanMin;
  const suctionLabel = SUCTION_LABELS[fan] ?? `Level ${fan}`;

  const sColor = stateColor(vstate);

  function toggleRoom(id: number) {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }

  function cleanSelected() {
    if (selected.size === 0) return;
    cmd('vacuum.cleanSegments', [...selected].join(','));
    setSelected(new Set());
  }

  const chipStyle = (isSel: boolean): React.CSSProperties => ({
    padding: '6px 12px',
    borderRadius: 999,
    fontSize: 13,
    fontFamily: 'var(--font-body)',
    cursor: 'pointer',
    userSelect: 'none',
    border: `1px solid ${isSel ? 'var(--accent-primary)' : 'var(--border-default)'}`,
    background: isSel ? 'var(--accent-primary-dim)' : 'var(--bg-hover)',
    color: isSel ? 'var(--accent-primary)' : 'var(--text-secondary)',
    transition: 'background 0.15s, border-color 0.15s, color 0.15s',
  });

  return (
    <div style={cardStyle}>
      {/* Header: name · state · battery */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
        <span style={{ width: 8, height: 8, borderRadius: '50%', background: sColor, flexShrink: 0 }} />
        <span style={{ fontFamily: 'var(--font-heading)', fontSize: 17, fontWeight: 700, color: 'var(--text-primary)' }}>
          {device.name}
        </span>
        <span style={{ fontSize: 12, fontWeight: 600, color: sColor, textTransform: 'capitalize' }}>{vstateLabel(vstate)}</span>
        <span style={{ marginLeft: 'auto', fontSize: 13, color: 'var(--text-secondary)' }}>
          {typeof battery === 'number' ? `${battery}%` : '—'}
        </span>
      </div>

      <div style={dividerStyle} />

      {/* Actions */}
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        {actionButton('Start', () => cmd('vacuum.start', true), true)}
        {actionButton('Pause', () => cmd('vacuum.pause', true))}
        {actionButton('Stop', () => cmd('vacuum.stop', true))}
        {actionButton('Return to dock', () => cmd('vacuum.dock', true))}
        {actionButton('Locate', () => cmd('vacuum.locate', true))}
      </div>

      {/* Suction */}
      <div style={dividerStyle} />
      <div style={sectionLabelStyle}>Suction · {suctionLabel}</div>
      <SliderControl
        value={fan} min={fanMin} max={fanMax}
        accentColor="var(--accent-primary)"
        onCommit={v => cmd('vacuum.fanPower', v)}
      />

      {/* Rooms */}
      {rooms.length > 0 && (
        <>
          <div style={dividerStyle} />
          <div style={sectionLabelStyle}>Clean rooms</div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
            {rooms.map(room => {
              const isSel = selected.has(room.id);
              return (
                <span
                  key={room.id}
                  role="button"
                  tabIndex={0}
                  aria-pressed={isSel}
                  style={chipStyle(isSel)}
                  onClick={() => toggleRoom(room.id)}
                  onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggleRoom(room.id); } }}
                >
                  {room.name}
                </span>
              );
            })}
          </div>
          <button
            type="button"
            className="btn-primary"
            disabled={selected.size === 0}
            style={{ marginTop: 12, opacity: selected.size === 0 ? 0.5 : 1 }}
            onClick={cleanSelected}
          >
            {selected.size === 0 ? 'Select rooms to clean' : `Clean ${selected.size} room${selected.size !== 1 ? 's' : ''}`}
          </button>
        </>
      )}

      {/* Routines */}
      {scenes.length > 0 && (
        <>
          <div style={dividerStyle} />
          <div style={sectionLabelStyle}>Routines</div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
            {scenes.map(scene => (
              <button
                key={scene.id}
                type="button"
                className="btn-secondary"
                onClick={() => cmd('vacuum.runScene', scene.id)}
              >
                {scene.name}
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  );
}

// Present a couple of internal states with clearer words for the header.
function vstateLabel(s: string): string {
  if (s === 'docked') return 'Docked';
  if (s === 'idle') return 'Idle';
  if (s === 'returning') return 'Returning';
  return s;
}
