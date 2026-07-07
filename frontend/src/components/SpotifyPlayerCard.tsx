import React from 'react';
import type { Device } from '../types';
import { SliderControl } from './SliderControl';

// The Spotify player for a Loxone Audio Server zone. Signature move: the album art sits inside a
// small "record" — a circular disc with a soft blurred halo cast from the same artwork — that spins
// while a track is playing and freezes the instant it's paused. That single piece of motion carries
// the whole play/pause state, so the rest of the card (progress, transport, volume, zone) can stay
// as quiet and metadata-driven as the app's other rich cards (see BambuPrinterCard / VacuumCard).

interface Props {
  device: Device;
  state: Record<string, unknown>;
  cmd: (capabilityKey: string, value: unknown) => void;
}

interface NowPlaying {
  title?: string;
  artist?: string;
  album?: string;
  artUrl?: string;
  progressMs?: number;
  durationMs?: number;
}

interface SpotifyZone {
  id: string | number;
  name: string;
  active?: boolean;
}

function fmtTime(ms: number): string {
  if (!Number.isFinite(ms) || ms < 0) return '0:00';
  const totalSec = Math.floor(ms / 1000);
  const m = Math.floor(totalSec / 60);
  const s = totalSec % 60;
  return `${m}:${String(s).padStart(2, '0')}`;
}

// Scoped keyframes for the spinning disc. Kept local to the component (rather than global.css) so
// this feature stays self-contained in one file; a plain <style> element works anywhere in the DOM.
const discStyleTag = `
  .spotify-disc-spinning { animation: spotify-disc-spin 16s linear infinite; }
  @keyframes spotify-disc-spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }
  @media (prefers-reduced-motion: reduce) {
    .spotify-disc-spinning { animation: none; }
  }
`;

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

export function SpotifyPlayerCard({ state, cmd }: Props) {
  const now = state['now_playing'] as NowPlaying | undefined;
  const zones = (state['zones'] as SpotifyZone[] | undefined) ?? [];
  const activeZoneRaw = state['zone'];
  const playing = state['playback'] === true;
  const volume = typeof state['volume'] === 'number' ? (state['volume'] as number) : 0;

  const title = now?.title?.trim() || null;
  const artist = now?.artist?.trim() || null;
  const album = now?.album?.trim() || null;
  const art = now?.artUrl || null;
  const progressMs = typeof now?.progressMs === 'number' ? now.progressMs : 0;
  const durationMs = typeof now?.durationMs === 'number' ? now.durationMs : 0;
  const pct = durationMs > 0 ? Math.max(0, Math.min(100, (progressMs / durationMs) * 100)) : 0;
  const hasTrack = Boolean(title || artist || art);

  const statusColor = playing ? 'var(--accent-green)' : hasTrack ? 'var(--accent-primary)' : 'var(--text-muted)';
  const statusLabel = playing ? 'Playing' : hasTrack ? 'Paused' : 'Nothing playing';

  function handleZoneChange(e: React.ChangeEvent<HTMLSelectElement>) {
    const val = e.target.value;
    const match = zones.find(z => String(z.id) === val);
    cmd('zone', match ? match.id : val);
  }

  return (
    <div style={cardStyle}>
      <style>{discStyleTag}</style>

      {/* Header: status + zone picker */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
        <span style={{ width: 9, height: 9, borderRadius: '50%', background: statusColor, flexShrink: 0 }} />
        <span style={{ fontFamily: 'var(--font-heading)', fontSize: 16, fontWeight: 700, color: statusColor }}>{statusLabel}</span>

        {zones.length > 0 ? (
          <select
            value={activeZoneRaw !== undefined && activeZoneRaw !== null ? String(activeZoneRaw) : ''}
            onChange={handleZoneChange}
            aria-label="Zone"
            style={{
              marginLeft: 'auto', maxWidth: '55%', padding: '6px 10px',
              background: 'var(--bg-hover)', color: 'var(--text-primary)',
              border: '1px solid var(--border-default)', borderRadius: 'var(--radius-sm)',
              fontFamily: 'var(--font-body)', fontSize: 13, cursor: 'pointer',
            }}
          >
            {activeZoneRaw === undefined && <option value="" disabled>Select zone</option>}
            {zones.map(z => (
              <option key={z.id} value={String(z.id)}>{z.active ? `● ${z.name}` : z.name}</option>
            ))}
          </select>
        ) : (
          <span style={{ marginLeft: 'auto', fontSize: 12, color: 'var(--text-muted)', fontFamily: 'var(--font-body)' }}>
            No zones found
          </span>
        )}
      </div>

      {/* Hero: disc + track info */}
      <div style={{ display: 'flex', gap: 18, marginTop: 18, alignItems: 'center', flexWrap: 'wrap' }}>
        <div style={{ position: 'relative', width: 96, height: 96, flexShrink: 0 }}>
          {art && (
            <img
              src={art}
              alt=""
              aria-hidden="true"
              style={{
                position: 'absolute', inset: -16, width: 'calc(100% + 32px)', height: 'calc(100% + 32px)',
                objectFit: 'cover', borderRadius: '50%', filter: 'blur(20px) saturate(1.3)',
                opacity: 0.4, zIndex: 0,
              }}
            />
          )}
          <div
            className={playing && art ? 'spotify-disc-spinning' : undefined}
            style={{
              position: 'relative', zIndex: 1, width: '100%', height: '100%', borderRadius: '50%',
              overflow: 'hidden', background: 'var(--bg-hover)', border: '1px solid var(--border-default)',
              display: 'flex', alignItems: 'center', justifyContent: 'center', boxShadow: 'var(--shadow-card)',
            }}
          >
            {art ? (
              <img src={art} alt={title ?? 'Album art'} style={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }} />
            ) : (
              <MusicNoteIcon size={30} color="var(--text-muted)" />
            )}
            {art && (
              <span style={{
                position: 'absolute', width: 14, height: 14, borderRadius: '50%',
                background: 'var(--bg-elevated)', border: '1px solid var(--border-default)',
              }} />
            )}
          </div>
        </div>

        <div style={{ flex: '1 1 160px', minWidth: 140, fontFamily: 'var(--font-body)' }}>
          <div style={{
            fontFamily: 'var(--font-heading)', fontSize: 18, fontWeight: 700, color: 'var(--text-primary)',
            overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
          }}>
            {title ?? 'Nothing playing'}
          </div>
          <div style={{
            fontSize: 14, color: 'var(--text-secondary)', marginTop: 3,
            overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
          }}>
            {artist ?? (zones.length > 0 ? 'Start something on Spotify' : 'Connect Spotify to control this zone')}
          </div>
          {album && (
            <div style={{
              fontSize: 12, color: 'var(--text-muted)', marginTop: 2,
              overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
            }}>
              {album}
            </div>
          )}
        </div>
      </div>

      {/* Progress */}
      <div style={{ marginTop: 16 }}>
        <div style={{ height: 4, borderRadius: 2, background: 'var(--bg-hover)', overflow: 'hidden' }}>
          <div style={{ height: '100%', width: `${pct}%`, borderRadius: 2, background: statusColor, transition: 'width 0.3s' }} />
        </div>
        <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 6, fontSize: 11, color: 'var(--text-muted)', fontFamily: 'var(--font-body)' }}>
          <span>{fmtTime(progressMs)}</span>
          <span>{fmtTime(durationMs)}</span>
        </div>
      </div>

      {/* Transport */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 16, marginTop: 18 }}>
        <button
          type="button"
          className="btn-secondary"
          aria-label="Previous track"
          onClick={() => cmd('track', 'previous')}
          style={transportBtnStyle}
        >
          <SkipBackIcon size={16} />
        </button>
        <button
          type="button"
          className="btn-primary"
          aria-label={playing ? 'Pause' : 'Play'}
          onClick={() => cmd('playback', !playing)}
          style={playBtnStyle}
        >
          {playing ? <PauseIcon size={20} /> : <PlayIcon size={20} />}
        </button>
        <button
          type="button"
          className="btn-secondary"
          aria-label="Next track"
          onClick={() => cmd('track', 'next')}
          style={transportBtnStyle}
        >
          <SkipForwardIcon size={16} />
        </button>
      </div>

      {/* Volume */}
      <div style={dividerStyle} />
      <div style={sectionLabelStyle}>Volume · {Math.round(volume)}%</div>
      <SliderControl
        value={volume} min={0} max={100}
        accentColor="var(--accent-primary)"
        onCommit={v => cmd('volume', v)}
      />
    </div>
  );
}

const transportBtnStyle: React.CSSProperties = {
  width: 40, height: 40, padding: 0, borderRadius: '50%',
  display: 'inline-flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0,
};

const playBtnStyle: React.CSSProperties = {
  width: 56, height: 56, padding: 0, borderRadius: '50%',
  display: 'inline-flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0,
};

// --- Icons (filled, inherit the button's text color) ---

function PlayIcon({ size = 18, color = 'currentColor' }: { size?: number; color?: string }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill={color}>
      <path d="M8 5v14l11-7z" />
    </svg>
  );
}

function PauseIcon({ size = 18, color = 'currentColor' }: { size?: number; color?: string }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill={color}>
      <rect x="6" y="5" width="4" height="14" rx="1" />
      <rect x="14" y="5" width="4" height="14" rx="1" />
    </svg>
  );
}

function SkipBackIcon({ size = 16, color = 'currentColor' }: { size?: number; color?: string }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polygon points="19 20 9 12 19 4 19 20" fill={color} stroke="none" />
      <line x1="5" y1="19" x2="5" y2="5" />
    </svg>
  );
}

function SkipForwardIcon({ size = 16, color = 'currentColor' }: { size?: number; color?: string }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polygon points="5 4 15 12 5 20 5 4" fill={color} stroke="none" />
      <line x1="19" y1="5" x2="19" y2="19" />
    </svg>
  );
}

function MusicNoteIcon({ size = 24, color = 'currentColor' }: { size?: number; color?: string }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M9 18V5l12-2v13" />
      <circle cx="6" cy="18" r="3" />
      <circle cx="18" cy="16" r="3" />
    </svg>
  );
}
