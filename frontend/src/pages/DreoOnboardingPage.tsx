import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { dreoLogin } from '../api/client';
import type { DreoDevice } from '../types';

// Mirrors RoborockOnboardingPage's visual language (shared global.css tokens, field
// styles, btn-primary/secondary) for a consistent onboarding experience. Dreo only
// supports password login, so there is no step bar and no email-code alternative.

// ---- Shared field styles (same as the Dyson/Roborock wizards) ----

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
  if (msg.startsWith('429')) return 'Dreo rate-limited the request. Wait a few minutes and try again.';
  if (msg.startsWith('401') || msg.toLowerCase().includes('unauthorized')) {
    return 'Dreo rejected the sign-in. Check your email and password and try again.';
  }
  if (msg.startsWith('502')) return 'Dreo cloud is unreachable. Check your connection and try again.';
  return msg;
}

// ---- Password fields (only path) ----

function PasswordForm({ email, setEmail, onConnected }: {
  email: string;
  setEmail: (v: string) => void;
  onConnected: (devices: DreoDevice[]) => void;
}) {
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const ready = email.trim() !== '' && password !== '';

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!ready) return;
    setLoading(true);
    setError(null);
    try {
      onConnected(await dreoLogin(email.trim(), password));
    } catch (err) {
      setError(friendlyError(err, 'Sign-in failed'));
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div>
        <label style={fieldLabelStyle}>Dreo account email</label>
        <input
          type="email"
          value={email}
          onChange={e => setEmail(e.target.value)}
          placeholder="you@example.com"
          style={inputStyle}
          onFocus={handleFocus}
          onBlur={handleBlur}
          autoComplete="email"
          autoFocus
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
            placeholder="Dreo app password"
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
          {loading ? 'Connecting…' : 'Connect'}
        </button>
      </div>

      <div style={{ fontSize: 12, color: 'var(--text-muted)', textAlign: 'center' }}>
        Sign in with your Dreo app account. The cloud is used once to pair your fans.
      </div>
    </form>
  );
}

// ---- Success state ----

function SuccessView({ deviceCount, onClose, onGoToSetup }: {
  deviceCount: number; onClose: () => void; onGoToSetup: () => void;
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
          Dreo connected
        </div>
        <div style={{ fontSize: 13, color: 'var(--text-muted)', lineHeight: 1.6, maxWidth: 300 }}>
          Found {deviceCount} fan{deviceCount !== 1 ? 's' : ''} — {deviceCount !== 1 ? "they're" : "it's"} now in Setup.
          Configure {deviceCount !== 1 ? 'each' : 'it'} to assign it to a room.
        </div>
      </div>

      <div style={{ display: 'flex', gap: 10 }}>
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

interface DreoOnboardingWizardProps {
  onClose: () => void;
  onSuccess: () => void;
}

export function DreoOnboardingWizard({ onClose, onSuccess }: DreoOnboardingWizardProps) {
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [devices, setDevices] = useState<DreoDevice[] | null>(null);

  function handleConnected(found: DreoDevice[]) {
    setDevices(found);
    onSuccess();
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
            <span style={{ fontSize: 22, lineHeight: 1 }}>🌀</span>
            <span style={{
              fontFamily: 'var(--font-heading)', fontSize: 17, fontWeight: 700,
              color: 'var(--text-primary)',
            }}>
              Connect Dreo
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
        {devices !== null ? (
          <SuccessView deviceCount={devices.length} onClose={onClose} onGoToSetup={handleGoToSetup} />
        ) : (
          <PasswordForm email={email} setEmail={setEmail} onConnected={handleConnected} />
        )}
      </div>
    </div>
  );
}
