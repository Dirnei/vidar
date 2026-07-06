import React, { useEffect, useState } from 'react';
import type { Device } from '../types';

// One consolidated view for a Bambu Lab printer. The hero fuses the live plate (camera snapshot)
// with the build progress; temperatures, filament (AMS) and controls sit quietly around it. Matches
// the app's rich-card conventions (see VacuumCard).

interface Props {
  device: Device;
  state: Record<string, unknown>;
  cmd: (capabilityKey: string, value: unknown) => void;
}

// gcode_state → accent + friendly label
function stateColor(s: string): string {
  switch (s) {
    case 'RUNNING': return 'var(--accent-green)';
    case 'PAUSE': return 'var(--accent-primary)';
    case 'FINISH': return 'var(--accent-blue)';
    case 'FAILED': return 'var(--accent-red)';
    case 'PREPARE': case 'SLICING': return 'var(--accent-teal)';
    default: return 'var(--text-muted)';
  }
}
function stateLabel(s: string): string {
  switch (s) {
    case 'RUNNING': return 'Printing';
    case 'PAUSE': return 'Paused';
    case 'FINISH': return 'Finished';
    case 'FAILED': return 'Failed';
    case 'PREPARE': return 'Preparing';
    case 'SLICING': return 'Slicing';
    case 'IDLE': return 'Idle';
    default: return s || 'Unknown';
  }
}

const SPEED_LABELS: Record<number, string> = { 1: 'Silent', 2: 'Standard', 3: 'Sport', 4: 'Ludicrous' };

function formatRemaining(min: number): string {
  if (min <= 0) return '—';
  const h = Math.floor(min / 60);
  const m = Math.round(min % 60);
  return h > 0 ? `${h}h ${m}m` : `${m}m`;
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
  textTransform: 'uppercase', letterSpacing: '0.07em', marginBottom: 10,
};
const dividerStyle: React.CSSProperties = { height: 1, background: 'var(--border-subtle)', margin: '18px 0' };

export function BambuPrinterCard({ device, state, cmd }: Props) {
  const [plateKey, setPlateKey] = useState(0);
  const [plateError, setPlateError] = useState(false);
  const [plateLoading, setPlateLoading] = useState(true);
  const [pending, setPending] = useState<Record<string, boolean>>({});
  const [pendingSpeed, setPendingSpeed] = useState<number | null>(null);

  const gstate = String(state['state'] ?? 'IDLE');
  const sColor = stateColor(gstate);
  const running = gstate === 'RUNNING';
  const paused = gstate === 'PAUSE';

  const progress = num(state['progress']) ?? 0;
  const remaining = num(state['time_remaining']);
  const layer = num(state['layer_current']);
  const layerTotal = num(state['layer_total']);
  const job = state['job_name'] ? String(state['job_name']) : null;
  const speed = num(state['print_speed_profile']) ?? 2;
  const lightOn = state['light_chamber'] === true;
  const hasError = state['has_error'] === true;
  const hmsError = state['hms_error'] ? String(state['hms_error']) : null;

  const plateSrc = `/api/devices/${device.id}/snapshot?t=${plateKey}`;
  const has = (k: string) => device.capabilities.some(c => c.key === k) || state[k] !== undefined;

  // AMS trays present in state (ams_tray_{n}_type/_color/_remain)
  const activeTray = num(state['ams_active_tray']);
  const trays = Array.from({ length: 16 }, (_, n) => n)
    .filter(n => state[`ams_tray_${n}_type`] !== undefined)
    .map(n => ({
      n,
      type: String(state[`ams_tray_${n}_type`]),
      color: state[`ams_tray_${n}_color`] ? String(state[`ams_tray_${n}_color`]) : null,
      remain: num(state[`ams_tray_${n}_remain`]),
    }));

  // Fire a command and show the button as "working" until the printer's own state reflects it
  // (or a fallback timeout, so a dropped command never leaves a button spinning forever).
  function press(key: string, value: unknown, fallbackMs = 8000) {
    setPending(p => ({ ...p, [key]: true }));
    cmd(key, value);
    window.setTimeout(() => setPending(p => (p[key] ? { ...p, [key]: false } : p)), fallbackMs);
  }
  useEffect(() => { setPending(p => (p.light_chamber ? { ...p, light_chamber: false } : p)); }, [lightOn]);
  useEffect(() => {
    setPending(p => (p.print_pause || p.print_resume || p.print_stop)
      ? { ...p, print_pause: false, print_resume: false, print_stop: false } : p);
  }, [gstate]);
  useEffect(() => { setPendingSpeed(null); }, [speed]);

  return (
    <div style={cardStyle}>
      {/* Header: state + current job (device name already shown on the page) */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
        <span style={{ width: 9, height: 9, borderRadius: '50%', background: sColor, flexShrink: 0 }} />
        <span style={{ fontFamily: 'var(--font-heading)', fontSize: 16, fontWeight: 700, color: sColor }}>{stateLabel(gstate)}</span>
        {job && (
          <span style={{ marginLeft: 'auto', fontSize: 13, color: 'var(--text-secondary)', fontFamily: 'var(--font-body)', maxWidth: '60%', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {job}
          </span>
        )}
      </div>

      {hasError && (
        <div style={{
          marginTop: 14, padding: '10px 14px', borderRadius: 'var(--radius-sm)',
          background: 'var(--accent-red-dim)', border: '1px solid var(--accent-red)',
          color: 'var(--accent-red)', fontSize: 13, fontFamily: 'var(--font-body)',
        }}>
          {hmsError ?? 'The printer reported an error.'}
        </div>
      )}

      {/* Hero: plate + progress */}
      <div style={{ display: 'flex', gap: 18, marginTop: 16, flexWrap: 'wrap' }}>
        {/* Plate (camera live view) */}
        <div style={{ position: 'relative', flex: '1 1 260px', minWidth: 240, aspectRatio: '4 / 3', background: 'var(--bg-hover)', borderRadius: 'var(--radius-sm)', overflow: 'hidden', border: '1px solid var(--border-subtle)' }}>
          {!plateError ? (
            <img
              key={plateKey}
              src={plateSrc}
              alt="Printer plate"
              onLoad={() => setPlateLoading(false)}
              onError={() => { setPlateLoading(false); setPlateError(true); }}
              style={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }}
            />
          ) : (
            <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'var(--text-muted)', fontSize: 12, textAlign: 'center', padding: 16, fontFamily: 'var(--font-body)' }}>
              No live view — enable LAN Mode Liveview on the printer
            </div>
          )}
          <button
            type="button"
            onClick={() => { setPlateError(false); setPlateLoading(true); setPlateKey(k => k + 1); }}
            disabled={plateLoading}
            aria-label="Refresh plate view"
            style={{ position: 'absolute', top: 8, right: 8, border: 'none', cursor: plateLoading ? 'default' : 'pointer', background: 'rgba(0,0,0,0.45)', color: '#fff', borderRadius: 'var(--radius-sm)', padding: '4px 9px', fontSize: 11, fontFamily: 'var(--font-body)', display: 'inline-flex', alignItems: 'center', gap: 5 }}
          >
            {plateLoading && <span style={{ width: 10, height: 10, borderRadius: '50%', border: '2px solid currentColor', borderTopColor: 'transparent', display: 'inline-block', animation: 'spin 0.7s linear infinite' }} />}
            {plateLoading ? 'Loading' : 'Refresh'}
          </button>
        </div>

        {/* Progress ring + stats */}
        <div style={{ flex: '1 1 200px', minWidth: 180, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 14 }}>
          <ProgressRing percent={progress} color={sColor} />
          <div style={{ display: 'flex', gap: 22, fontFamily: 'var(--font-body)', textAlign: 'center' }}>
            <Stat label="Remaining" value={remaining != null ? formatRemaining(remaining) : '—'} />
            {layer != null && <Stat label="Layer" value={layerTotal != null ? `${layer} / ${layerTotal}` : String(layer)} />}
          </div>
          <div style={{ fontSize: 12, color: 'var(--text-muted)', fontFamily: 'var(--font-body)' }}>
            {SPEED_LABELS[speed] ?? `Speed ${speed}`} speed
          </div>
        </div>
      </div>

      {/* Temperatures & fans (read-only) */}
      <div style={dividerStyle} />
      <div style={sectionLabelStyle}>Temperatures &amp; Fans</div>
      <div style={{ display: 'flex', gap: 22, rowGap: 14, flexWrap: 'wrap' }}>
        <Temp label="Nozzle" current={num(state['nozzle_temp'])} target={num(state['nozzle_target'])} dot="var(--accent-red)" />
        <Temp label="Bed" current={num(state['bed_temp'])} target={num(state['bed_target'])} dot="var(--accent-primary)" />
        {has('chamber_temp') && <Temp label="Chamber" current={num(state['chamber_temp'])} target={null} dot="var(--accent-teal)" />}
        {has('fan_cooling') && <Reading label="Part fan" value={num(state['fan_cooling'])} unit="%" dot="var(--accent-blue)" />}
        {has('fan_aux') && <Reading label="Aux fan" value={num(state['fan_aux'])} unit="%" dot="var(--accent-blue)" />}
        {has('fan_chamber') && <Reading label="Chamber fan" value={num(state['fan_chamber'])} unit="%" dot="var(--accent-blue)" />}
      </div>

      {/* AMS filament */}
      {trays.length > 0 && (
        <>
          <div style={dividerStyle} />
          <div style={sectionLabelStyle}>Filament (AMS)</div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10 }}>
            {trays.map(t => {
              const active = activeTray === t.n;
              const swatch = t.color ? `#${t.color.slice(0, 6)}` : 'var(--text-muted)';
              return (
                <div key={t.n} style={{
                  display: 'flex', alignItems: 'center', gap: 9, padding: '7px 12px',
                  borderRadius: 999, background: 'var(--bg-hover)', fontFamily: 'var(--font-body)',
                  border: `1px solid ${active ? 'var(--accent-primary)' : 'var(--border-subtle)'}`,
                }}>
                  <span style={{ width: 14, height: 14, borderRadius: '50%', background: swatch, border: '1px solid rgba(255,255,255,0.25)', flexShrink: 0 }} />
                  <span style={{ fontSize: 13, color: 'var(--text-secondary)' }}>{t.type}</span>
                  {t.remain != null && <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>{Math.round(t.remain)}%</span>}
                </div>
              );
            })}
          </div>
        </>
      )}

      {/* Controls */}
      <div style={dividerStyle} />
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        {paused
          ? <Btn label="Resume" pending={pending.print_resume} onClick={() => press('print_resume', true)} primary />
          : <Btn label="Pause" pending={pending.print_pause} onClick={() => press('print_pause', true)} disabled={!running} primary />}
        <Btn label="Stop" pending={pending.print_stop} onClick={() => press('print_stop', true)} disabled={!running && !paused} />
        <Btn label={lightOn ? 'Light off' : 'Light on'} pending={pending.light_chamber} onClick={() => press('light_chamber', !lightOn)} />
        <Btn label="Home" pending={pending.home} onClick={() => press('home', true, 3000)} />
      </div>

      {/* Speed profile */}
      {has('print_speed_profile') && (
        <>
          <div style={{ ...sectionLabelStyle, marginTop: 16 }}>Print speed</div>
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            {[1, 2, 3, 4].map(lvl => {
              const sel = (pendingSpeed ?? speed) === lvl;
              return (
                <button
                  key={lvl}
                  type="button"
                  onClick={() => { setPendingSpeed(lvl); cmd('print_speed_profile', lvl); }}
                  style={{
                    padding: '6px 14px', borderRadius: 999, fontSize: 13, cursor: 'pointer',
                    fontFamily: 'var(--font-body)',
                    border: `1px solid ${sel ? 'var(--accent-primary)' : 'var(--border-default)'}`,
                    background: sel ? 'var(--accent-primary-dim)' : 'var(--bg-hover)',
                    color: sel ? 'var(--accent-primary)' : 'var(--text-secondary)',
                  }}
                >
                  {SPEED_LABELS[lvl]}
                </button>
              );
            })}
          </div>
        </>
      )}

    </div>
  );
}

// Big circular progress with the percentage in the centre.
function ProgressRing({ percent, color, size = 156, stroke = 12 }: { percent: number; color: string; size?: number; stroke?: number }) {
  const pct = Math.max(0, Math.min(100, percent));
  const r = (size - stroke) / 2;
  const circ = 2 * Math.PI * r;
  const offset = circ * (1 - pct / 100);
  const mid = size / 2;
  return (
    <div style={{ position: 'relative', width: size, height: size }}>
      <svg width={size} height={size} style={{ display: 'block', transform: 'rotate(-90deg)' }}>
        <circle cx={mid} cy={mid} r={r} fill="none" stroke="var(--bg-hover)" strokeWidth={stroke} />
        <circle
          cx={mid} cy={mid} r={r} fill="none" stroke={color} strokeWidth={stroke}
          strokeDasharray={circ} strokeDashoffset={offset} strokeLinecap="round"
          style={{ transition: 'stroke-dashoffset 0.6s ease, stroke 0.3s ease' }}
        />
      </svg>
      <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <span style={{ fontFamily: 'var(--font-heading)', fontSize: 42, fontWeight: 700, lineHeight: 1, color: 'var(--text-primary)' }}>
          {Math.round(pct)}
          <span style={{ fontSize: 17, color: 'var(--text-muted)', fontWeight: 600 }}>%</span>
        </span>
      </div>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div style={{ fontSize: 11, color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: '0.06em' }}>{label}</div>
      <div style={{ fontSize: 15, color: 'var(--text-primary)', fontWeight: 600 }}>{value}</div>
    </div>
  );
}

function Temp({ label, current, target, dot }: { label: string; current: number | null; target: number | null; dot: string }) {
  const heating = current != null && target != null && target > 0 && current < target - 1;
  return (
    <div style={{ fontFamily: 'var(--font-body)' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 3 }}>
        <span style={{ width: 7, height: 7, borderRadius: '50%', background: dot }} />
        <span style={{ fontSize: 11, color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: '0.06em' }}>{label}</span>
      </div>
      <div style={{ fontFamily: 'var(--font-heading)', fontSize: 20, fontWeight: 700, color: 'var(--text-primary)' }}>
        {current != null ? `${Math.round(current)}°` : '—'}
        {target != null && target > 0 && (
          <span style={{ fontSize: 13, fontWeight: 500, color: heating ? 'var(--accent-primary)' : 'var(--text-muted)', fontFamily: 'var(--font-body)' }}>
            {' → '}{Math.round(target)}°
          </span>
        )}
      </div>
    </div>
  );
}

function Btn({ label, onClick, primary, disabled, pending }: { label: string; onClick: () => void; primary?: boolean; disabled?: boolean; pending?: boolean }) {
  return (
    <button
      type="button"
      className={primary ? 'btn-primary' : 'btn-secondary'}
      disabled={disabled || pending}
      onClick={onClick}
      style={{ flex: '1 1 auto', minWidth: 88, opacity: disabled ? 0.45 : 1, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: 7 }}
    >
      {pending && (
        <span style={{ width: 13, height: 13, borderRadius: '50%', border: '2px solid currentColor', borderTopColor: 'transparent', display: 'inline-block', animation: 'spin 0.7s linear infinite' }} />
      )}
      {label}
    </button>
  );
}

// Read-only value block (fans), styled to match the temperature blocks.
function Reading({ label, value, unit, dot }: { label: string; value: number | null; unit: string; dot: string }) {
  return (
    <div style={{ fontFamily: 'var(--font-body)' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 3 }}>
        <span style={{ width: 7, height: 7, borderRadius: '50%', background: dot }} />
        <span style={{ fontSize: 11, color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: '0.06em' }}>{label}</span>
      </div>
      <div style={{ fontFamily: 'var(--font-heading)', fontSize: 20, fontWeight: 700, color: 'var(--text-primary)' }}>
        {value != null ? `${Math.round(value)}${unit}` : '—'}
      </div>
    </div>
  );
}
