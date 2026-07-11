import React from 'react';
import type { Device } from '../types';
import { SliderControl } from './SliderControl';

// Two Spotify cards behind one dispatcher:
//  - The central "Spotify Player" device (nativeId "player") → global now-playing + transport + a
//    device picker (dropdown) that transfers the stream anywhere ("control from one place").
//  - A per-speaker device → controls that one Connect device; "Play here" (transfer) when it is not
//    the active target, full controls when it is ("move it here").
// Both reuse the spinning-disc hero: the album art sits inside a "record" that spins while playing
// and freezes when paused.

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

const discStyleTag = `
  .spotify-disc-spinning { animation: spotify-disc-spin 16s linear infinite; }
  @keyframes spotify-disc-spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }
  .spotify-refresh-spinning { animation: spotify-refresh-spin 0.8s linear infinite; }
  @keyframes spotify-refresh-spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }
  @media (prefers-reduced-motion: reduce) {
    .spotify-disc-spinning, .spotify-refresh-spinning { animation: none; }
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

// Shared "live while the card is open" logic: a ~10s refresh tick (client-driven — the worker never
// polls in the background), local progress interpolation between fetches, and a refresh button state.
function useSpotifyLive(cmd: Props['cmd'], serverProgressMs: number, durationMs: number, ticking: boolean) {
  const cmdRef = React.useRef(cmd);
  cmdRef.current = cmd;
  React.useEffect(() => {
    cmdRef.current('refresh', true);
    const t = window.setInterval(() => cmdRef.current('refresh', true), 10000);
    return () => window.clearInterval(t);
  }, []);

  const baseline = React.useRef({ progress: serverProgressMs, at: Date.now() });
  const [displayProgressMs, setDisplayProgressMs] = React.useState(serverProgressMs);
  React.useEffect(() => {
    baseline.current = { progress: serverProgressMs, at: Date.now() };
    setDisplayProgressMs(serverProgressMs);
  }, [serverProgressMs, durationMs]);
  React.useEffect(() => {
    if (!ticking) return;
    const t = window.setInterval(() => {
      const p = baseline.current.progress + (Date.now() - baseline.current.at);
      setDisplayProgressMs(durationMs > 0 ? Math.min(p, durationMs) : p);
    }, 1000);
    return () => window.clearInterval(t);
  }, [ticking, durationMs]);

  const [refreshing, setRefreshing] = React.useState(false);
  function refresh() {
    cmd('refresh', true);
    setRefreshing(true);
    window.setTimeout(() => setRefreshing(false), 1000);
  }
  return { displayProgressMs, refresh, refreshing };
}

function RefreshButton({ onClick, spinning }: { onClick: () => void; spinning: boolean }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label="Refresh"
      title="Refresh"
      style={{
        flexShrink: 0, width: 30, height: 30, padding: 0, borderRadius: '50%',
        display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
        background: 'var(--bg-hover)', color: 'var(--text-secondary)',
        border: '1px solid var(--border-default)', cursor: 'pointer',
      }}
    >
      <RefreshIcon size={15} className={spinning ? 'spotify-refresh-spinning' : undefined} />
    </button>
  );
}

// Shared disc + track-info hero.
function TrackHero({ now, spinning }: { now: NowPlaying | undefined; spinning: boolean }) {
  const title = now?.title?.trim() || null;
  const artist = now?.artist?.trim() || null;
  const album = now?.album?.trim() || null;
  const art = now?.artUrl || null;
  return (
    <div style={{ display: 'flex', gap: 18, marginTop: 18, alignItems: 'center', flexWrap: 'wrap' }}>
      <div style={{ position: 'relative', width: 96, height: 96, flexShrink: 0 }}>
        {art && (
          <img
            src={art} alt="" aria-hidden="true"
            style={{
              position: 'absolute', inset: -16, width: 'calc(100% + 32px)', height: 'calc(100% + 32px)',
              objectFit: 'cover', borderRadius: '50%', filter: 'blur(20px) saturate(1.3)', opacity: 0.4, zIndex: 0,
            }}
          />
        )}
        <div
          className={spinning && art ? 'spotify-disc-spinning' : undefined}
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
          {artist ?? 'Start something on Spotify'}
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
  );
}

function ProgressRow({ progressMs, durationMs, color }: { progressMs: number; durationMs: number; color: string }) {
  const pct = durationMs > 0 ? Math.max(0, Math.min(100, (progressMs / durationMs) * 100)) : 0;
  return (
    <div style={{ marginTop: 16 }}>
      <div style={{ height: 4, borderRadius: 2, background: 'var(--bg-hover)', overflow: 'hidden' }}>
        <div style={{ height: '100%', width: `${pct}%`, borderRadius: 2, background: color, transition: 'width 0.3s' }} />
      </div>
      <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 6, fontSize: 11, color: 'var(--text-muted)', fontFamily: 'var(--font-body)' }}>
        <span>{fmtTime(progressMs)}</span>
        <span>{fmtTime(durationMs)}</span>
      </div>
    </div>
  );
}

function TransportRow({ playing, cmd }: { playing: boolean; cmd: Props['cmd'] }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 16, marginTop: 18 }}>
      <button type="button" className="btn-secondary" aria-label="Previous track" onClick={() => cmd('track', 'previous')} style={transportBtnStyle}>
        <SkipBackIcon size={16} />
      </button>
      <button type="button" className="btn-primary" aria-label={playing ? 'Pause' : 'Play'} onClick={() => cmd('playback', !playing)} style={playBtnStyle}>
        {playing ? <PauseIcon size={20} /> : <PlayIcon size={20} />}
      </button>
      <button type="button" className="btn-secondary" aria-label="Next track" onClick={() => cmd('track', 'next')} style={transportBtnStyle}>
        <SkipForwardIcon size={16} />
      </button>
    </div>
  );
}

function VolumeRow({ volume, cmd }: { volume: number; cmd: Props['cmd'] }) {
  return (
    <>
      <div style={dividerStyle} />
      <div style={sectionLabelStyle}>Volume · {Math.round(volume)}%</div>
      <SliderControl value={volume} min={0} max={100} accentColor="var(--accent-primary)" onCommit={v => cmd('volume', v)} />
    </>
  );
}

// --- Central "Spotify Player": global controls + device picker (transfer the stream) ---
function CentralPlayerCard({ state, cmd }: { state: Props['state']; cmd: Props['cmd'] }) {
  const now = state['now_playing'] as NowPlaying | undefined;
  const zones = (state['zones'] as SpotifyZone[] | undefined) ?? [];
  const activeZoneRaw = state['zone'];
  const playing = state['playback'] === true;
  const volume = typeof state['volume'] === 'number' ? (state['volume'] as number) : 0;
  const serverProgressMs = typeof now?.progressMs === 'number' ? now.progressMs : 0;
  const durationMs = typeof now?.durationMs === 'number' ? now.durationMs : 0;
  const hasTrack = Boolean(now?.title || now?.artist || now?.artUrl);

  const { displayProgressMs, refresh, refreshing } = useSpotifyLive(cmd, serverProgressMs, durationMs, playing);

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
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
        <span style={{ width: 9, height: 9, borderRadius: '50%', background: statusColor, flexShrink: 0 }} />
        <span style={{ fontFamily: 'var(--font-heading)', fontSize: 16, fontWeight: 700, color: statusColor }}>{statusLabel}</span>

        <div style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 6, maxWidth: '62%', minWidth: 0 }}>
          {zones.length > 0 ? (
            <select
              value={activeZoneRaw !== undefined && activeZoneRaw !== null ? String(activeZoneRaw) : ''}
              onChange={handleZoneChange}
              aria-label="Device"
              style={{
                minWidth: 0, flex: '0 1 auto', padding: '6px 10px',
                background: 'var(--bg-hover)', color: 'var(--text-primary)',
                border: '1px solid var(--border-default)', borderRadius: 'var(--radius-sm)',
                fontFamily: 'var(--font-body)', fontSize: 13, cursor: 'pointer',
                overflow: 'hidden', textOverflow: 'ellipsis',
              }}
            >
              {activeZoneRaw === undefined && <option value="" disabled>Select device</option>}
              {zones.map(z => (
                <option key={z.id} value={String(z.id)}>{z.active ? `● ${z.name}` : z.name}</option>
              ))}
            </select>
          ) : (
            <span style={{ fontSize: 12, color: 'var(--text-muted)', fontFamily: 'var(--font-body)' }}>No devices</span>
          )}
          <RefreshButton onClick={refresh} spinning={refreshing} />
        </div>
      </div>

      <TrackHero now={now} spinning={playing} />
      <ProgressRow progressMs={displayProgressMs} durationMs={durationMs} color={statusColor} />
      <TransportRow playing={playing} cmd={cmd} />
      <VolumeRow volume={volume} cmd={cmd} />
    </div>
  );
}

// --- Per-speaker device: full controls when active, "Play here" when not ---
function SpeakerCard({ state, cmd }: { state: Props['state']; cmd: Props['cmd'] }) {
  const now = state['now_playing'] as NowPlaying | undefined;
  const isActive = state['active'] === true;
  const playing = state['playback'] === true;
  const volume = typeof state['volume'] === 'number' ? (state['volume'] as number) : 0;
  const serverProgressMs = typeof now?.progressMs === 'number' ? now.progressMs : 0;
  const durationMs = typeof now?.durationMs === 'number' ? now.durationMs : 0;
  const hasTrack = Boolean(now?.title || now?.artist || now?.artUrl);

  const { displayProgressMs, refresh, refreshing } = useSpotifyLive(cmd, serverProgressMs, durationMs, isActive && playing);

  const statusColor = isActive && playing ? 'var(--accent-green)' : isActive && hasTrack ? 'var(--accent-primary)' : 'var(--text-muted)';
  const statusLabel = !isActive ? 'Not playing here' : playing ? 'Playing' : hasTrack ? 'Paused' : 'Nothing playing';

  return (
    <div style={cardStyle}>
      <style>{discStyleTag}</style>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
        <span style={{ width: 9, height: 9, borderRadius: '50%', background: statusColor, flexShrink: 0 }} />
        <span style={{ fontFamily: 'var(--font-heading)', fontSize: 16, fontWeight: 700, color: statusColor }}>{statusLabel}</span>
        <div style={{ marginLeft: 'auto' }}>
          <RefreshButton onClick={refresh} spinning={refreshing} />
        </div>
      </div>

      <TrackHero now={isActive ? now : undefined} spinning={isActive && playing} />

      {isActive ? (
        <>
          <ProgressRow progressMs={displayProgressMs} durationMs={durationMs} color={statusColor} />
          <TransportRow playing={playing} cmd={cmd} />
        </>
      ) : (
        <div style={{ display: 'flex', justifyContent: 'center', marginTop: 18 }}>
          <button type="button" className="btn-primary" onClick={() => cmd('playback', true)} style={{ padding: '10px 24px', borderRadius: 999, fontWeight: 600 }}>
            Play here
          </button>
        </div>
      )}

      <VolumeRow volume={volume} cmd={cmd} />
    </div>
  );
}

export function SpotifyPlayerCard({ device, state, cmd }: Props) {
  // The central "Spotify Player" device exposes a `zone` (device-picker) capability; per-speaker
  // devices expose `active` instead. Dispatch on that (the accepted-device API doesn't carry nativeId).
  const isCentral = device.capabilities?.some(c => c.key === 'zone') ?? false;
  return isCentral
    ? <CentralPlayerCard state={state} cmd={cmd} />
    : <SpeakerCard state={state} cmd={cmd} />;
}

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

function RefreshIcon({ size = 16, color = 'currentColor', className }: { size?: number; color?: string; className?: string }) {
  return (
    <svg className={className} width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M21 12a9 9 0 1 1-2.64-6.36" />
      <polyline points="21 3 21 9 15 9" />
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

const transportBtnStyle: React.CSSProperties = {
  width: 40, height: 40, padding: 0, borderRadius: '50%',
  display: 'inline-flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0,
};

const playBtnStyle: React.CSSProperties = {
  width: 56, height: 56, padding: 0, borderRadius: '50%',
  display: 'inline-flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0,
};
