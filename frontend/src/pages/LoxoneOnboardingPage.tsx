import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { loxoneAddMiniserver, loxoneGetAccount, loxoneRemoveMiniserver } from '../api/client';
import type { LoxoneMiniserverSummary, LoxoneProbeResult } from '../types';

// Mirrors DreoOnboardingPage's visual language (shared global.css tokens, field
// styles, btn-primary/secondary) for a consistent onboarding experience. Unlike Dreo's
// single cloud account, a Loxone install can have several Miniservers on the LAN, so
// this wizard is repeatable: it lists what's already been added and lets you keep
// adding more without closing the modal.

// ---- Shared field styles (same as the Dyson/Roborock/Dreo wizards) ----

const fieldLabelStyle: React.CSSProperties = {
  fontSize: 11,
  fontWeight: 600,
  color: 'var(--text-muted)',
  textTransform: 'uppercase',
  letterSpacing: '0.06em',
  display: 'block',
  marginBottom: 5,
};

const inputStyle: React.CSSProperties = {
  background: 'var(--bg-hover)',
  border: '1px solid var(--border-default)',
  borderRadius: 'var(--radius-sm)',
  padding: '9px 13px',
  color: 'var(--text-primary)',
  fontFamily: 'var(--font-body)',
  fontSize: 14,
  outline: 'none',
  width: '100%',
  boxSizing: 'border-box' as const,
  transition: 'border-color 0.15s, box-shadow 0.15s',
};

function handleFocus(e: React.FocusEvent<HTMLInputElement>) {
  e.currentTarget.style.borderColor = 'var(--accent-primary)';
  e.currentTarget.style.boxShadow = '0 0 0 3px color-mix(in srgb, var(--accent-primary) 20%, transparent)';
}

function handleBlur(e: React.FocusEvent<HTMLInputElement>) {
  e.currentTarget.style.borderColor = 'var(--border-default)';
  e.currentTarget.style.boxShadow = 'none';
}

// ---- Error banner + message mapping ----

function ErrorBanner({ message }: { message: string }) {
  return (
    <div style={{
      background: 'color-mix(in srgb, var(--accent-red) 12%, transparent)',
      border: '1px solid color-mix(in srgb, var(--accent-red) 35%, transparent)',
      borderRadius: 'var(--radius-sm)',
      padding: '10px 14px',
      fontSize: 13,
      color: 'var(--accent-red)',
      marginTop: 4,
    }}>
      {message}
    </div>
  );
}

function friendlyError(err: unknown, fallback: string): string {
  const msg = err instanceof Error ? err.message : fallback;
  if (msg.startsWith('401') || msg.toLowerCase().includes('unauthorized')) {
    return 'The Miniserver rejected those credentials. Check the username and password and try again.';
  }
  if (msg.startsWith('409') || msg.toLowerCase().includes('already')) {
    return 'That Miniserver is already added.';
  }
  if (msg.startsWith('502') || msg.startsWith('504') || msg.toLowerCase().includes('unreachable')) {
    return "Couldn't reach the Miniserver. Check the host/IP and that it's on the network.";
  }
  return msg;
}

// ---- Already-added Miniservers list ----

function MiniserverList({ miniservers, onRemove }: {
  miniservers: LoxoneMiniserverSummary[];
  onRemove: (serial: string) => void;
}) {
  const [removing, setRemoving] = useState<string | null>(null);
  const [removeError, setRemoveError] = useState<string | null>(null);

  async function handleRemove(serial: string) {
    setRemoving(serial);
    setRemoveError(null);
    try {
      await loxoneRemoveMiniserver(serial);
      onRemove(serial);
    } catch (err) {
      setRemoveError(friendlyError(err, 'Failed to remove Miniserver'));
    } finally {
      setRemoving(null);
    }
  }

  if (miniservers.length === 0) return null;

  return (
    <div style={{ marginBottom: 4 }}>
      <div style={fieldLabelStyle}>Added Miniservers</div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        {miniservers.map(ms => (
          <div
            key={ms.serial}
            style={{
              display: 'flex', alignItems: 'center', gap: 10,
              background: 'var(--bg-hover)', border: '1px solid var(--border-default)',
              borderRadius: 'var(--radius-sm)', padding: '8px 12px',
            }}
          >
            <span style={{ width: 6, height: 6, borderRadius: '50%', background: 'var(--accent-green)', flexShrink: 0 }} />
            <div style={{ minWidth: 0, flex: 1 }}>
              <div style={{ fontSize: 13, color: 'var(--text-primary)', fontWeight: 500 }}>{ms.serial}</div>
              <code style={{ fontSize: 11, color: 'var(--text-muted)' }}>{ms.host}</code>
            </div>
            <button
              type="button"
              onClick={() => handleRemove(ms.serial)}
              disabled={removing === ms.serial}
              style={{
                background: 'none', border: 'none', cursor: 'pointer', fontSize: 12,
                color: 'var(--text-muted)', padding: '4px 6px', fontFamily: 'var(--font-body)',
                opacity: removing === ms.serial ? 0.5 : 1,
              }}
            >
              {removing === ms.serial ? 'removing…' : 'remove'}
            </button>
          </div>
        ))}
      </div>
      {removeError && <ErrorBanner message={removeError} />}
    </div>
  );
}

// ---- Add-Miniserver form ----

function MiniserverForm({ onAdded }: { onAdded: (result: LoxoneProbeResult, host: string) => void }) {
  const [host, setHost] = useState('');
  const [user, setUser] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const ready = host.trim() !== '' && user.trim() !== '' && password !== '';

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!ready) return;
    setLoading(true);
    setError(null);
    try {
      const result = await loxoneAddMiniserver(host.trim(), user.trim(), password);
      onAdded(result, host.trim());
    } catch (err) {
      setError(friendlyError(err, 'Failed to add Miniserver'));
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div>
        <label style={fieldLabelStyle}>Miniserver host / IP</label>
        <input
          type="text"
          value={host}
          onChange={e => setHost(e.target.value)}
          placeholder="e.g. 192.168.1.77"
          style={inputStyle}
          onFocus={handleFocus}
          onBlur={handleBlur}
          autoComplete="off"
          autoFocus
          required
        />
      </div>

      <div>
        <label style={fieldLabelStyle}>Username</label>
        <input
          type="text"
          value={user}
          onChange={e => setUser(e.target.value)}
          placeholder="Miniserver username"
          style={inputStyle}
          onFocus={handleFocus}
          onBlur={handleBlur}
          autoComplete="username"
          required
        />
      </div>

      <div>
        <label style={fieldLabelStyle}>Password</label>
        <div style={{ position: 'relative' }}>
          <input
            type={showPassword ? 'text' : 'password'}
            value={password}
            onChange={e => setPassword(e.target.value)}
            placeholder="Miniserver password"
            style={{ ...inputStyle, paddingRight: 54 }}
            onFocus={handleFocus}
            onBlur={handleBlur}
            autoComplete="current-password"
            required
          />
          <button
            type="button"
            onClick={() => setShowPassword(v => !v)}
            style={{
              position: 'absolute', right: 10, top: '50%', transform: 'translateY(-50%)',
              background: 'none', border: 'none', cursor: 'pointer', fontSize: 12,
              color: 'var(--text-muted)', padding: '0 4px', fontFamily: 'var(--font-body)',
            }}
          >
            {showPassword ? 'hide' : 'show'}
          </button>
        </div>
      </div>

      {error && <ErrorBanner message={error} />}

      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'flex-end', marginTop: 4 }}>
        <button
          type="submit"
          className="btn-primary"
          disabled={loading || !ready}
          style={{ opacity: loading || !ready ? 0.5 : 1 }}
        >
          {loading ? 'Connecting…' : 'Add Miniserver'}
        </button>
      </div>

      <div style={{ fontSize: 12, color: 'var(--text-muted)', textAlign: 'center' }}>
        Vidar talks to the Miniserver directly over your local network — nothing leaves your home.
      </div>
    </form>
  );
}

// ---- Success state ----

function SuccessView({ probe, onAddAnother, onClose, onGoToSetup }: {
  probe: LoxoneProbeResult;
  onAddAnother: () => void;
  onClose: () => void;
  onGoToSetup: () => void;
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 20, padding: '8px 0' }}>
      <div style={{
        width: 56, height: 56, borderRadius: '50%',
        background: 'color-mix(in srgb, var(--accent-green) 15%, transparent)',
        border: '2px solid color-mix(in srgb, var(--accent-green) 40%, transparent)',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
      }}>
        <svg width="24" height="19" viewBox="0 0 24 19" fill="none">
          <path d="M2 10L9 17L22 2" stroke="var(--accent-green)" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </div>

      <div style={{ textAlign: 'center' }}>
        <div style={{
          fontFamily: 'var(--font-heading)', fontSize: 18, fontWeight: 700,
          color: 'var(--text-primary)', marginBottom: 8,
        }}>
          Miniserver added
        </div>
        <div style={{ fontSize: 13, color: 'var(--text-muted)', lineHeight: 1.6, maxWidth: 320 }}>
          Found {probe.controlCount} control{probe.controlCount !== 1 ? 's' : ''} across {probe.roomCount} room{probe.roomCount !== 1 ? 's' : ''} —
          {' '}now in Setup. Configure them to assign each to a room.
        </div>
      </div>

      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', justifyContent: 'center' }}>
        <button type="button" className="btn-secondary" onClick={onAddAnother}>
          Add another Miniserver
        </button>
        <button type="button" className="btn-secondary" onClick={onClose}>
          Close
        </button>
        <button type="button" className="btn-primary" onClick={onGoToSetup} style={{ minWidth: 120 }}>
          Go to Setup
        </button>
      </div>
    </div>
  );
}

// ---- Main wizard ----

interface LoxoneOnboardingWizardProps {
  onClose: () => void;
  onSuccess: () => void;
}

export function LoxoneOnboardingWizard({ onClose, onSuccess }: LoxoneOnboardingWizardProps) {
  const navigate = useNavigate();
  const [miniservers, setMiniservers] = useState<LoxoneMiniserverSummary[]>([]);
  const [lastProbe, setLastProbe] = useState<LoxoneProbeResult | null>(null);

  // Load the already-added Miniservers once on mount.
  useEffect(() => {
    loxoneGetAccount().then(acc => setMiniservers(acc.miniservers)).catch(() => setMiniservers([]));
  }, []);

  function handleAdded(result: LoxoneProbeResult, host: string) {
    setMiniservers(prev => [...prev.filter(m => m.serial !== result.serial), { serial: result.serial, host }]);
    setLastProbe(result);
    onSuccess();
  }

  function handleRemoved(serial: string) {
    setMiniservers(prev => prev.filter(m => m.serial !== serial));
    onSuccess();
  }

  function handleAddAnother() {
    setLastProbe(null);
  }

  function handleGoToSetup() {
    onClose();
    navigate('/discovered');
  }

  return (
    <div
      className="modal-overlay"
      onClick={e => e.target === e.currentTarget && onClose()}
    >
      <div
        className="modal-dialog"
        style={{
          width: 440,
          maxWidth: 'calc(100vw - 32px)',
          maxHeight: 'calc(100vh - 48px)',
          overflowY: 'auto',
        }}
      >
        {/* Header */}
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 24 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={{ fontSize: 22, lineHeight: 1 }}>🟩</span>
            <span style={{
              fontFamily: 'var(--font-heading)', fontSize: 17, fontWeight: 700,
              color: 'var(--text-primary)',
            }}>
              Add a Loxone Miniserver
            </span>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            style={{
              background: 'none', border: 'none', padding: '4px 6px', cursor: 'pointer',
              color: 'var(--text-muted)', fontSize: 18, lineHeight: 1,
              borderRadius: 'var(--radius-sm)', transition: 'color 0.15s',
            }}
            onMouseEnter={e => (e.currentTarget.style.color = 'var(--text-primary)')}
            onMouseLeave={e => (e.currentTarget.style.color = 'var(--text-muted)')}
          >
            ✕
          </button>
        </div>

        {/* Content */}
        {lastProbe !== null ? (
          <SuccessView
            probe={lastProbe}
            onAddAnother={handleAddAnother}
            onClose={onClose}
            onGoToSetup={handleGoToSetup}
          />
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
            <MiniserverList miniservers={miniservers} onRemove={handleRemoved} />
            <MiniserverForm onAdded={handleAdded} />
          </div>
        )}
      </div>
    </div>
  );
}
