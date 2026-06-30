import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { roborockLogin, roborockRequestCode, roborockCodeLogin } from '../api/client';
import type { RoborockDevice } from '../types';

// Mirrors DysonOnboardingPage's visual language (shared global.css tokens, field
// styles, btn-primary/secondary) for a consistent onboarding experience. Roborock's
// password login is a single step, so there is no step bar — the email-code path is
// offered inline as an alternative rather than as a forced multi-step sequence.

// ---- Shared field styles (same as the Dyson wizard) ----

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

const switchLinkStyle: React.CSSProperties = {
  background: 'none',
  border: 'none',
  padding: 0,
  fontSize: 12,
  fontFamily: 'var(--font-body)',
  color: 'var(--accent-primary)',
  cursor: 'pointer',
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
  if (msg.startsWith('429')) return 'Roborock rate-limited the request. Wait a few minutes and try again.';
  if (msg.startsWith('401') || msg.toLowerCase().includes('unauthorized')) {
    return 'Roborock rejected the sign-in. Check your email and password (or code) and try again.';
  }
  if (msg.startsWith('502')) return 'Roborock cloud is unreachable. Check your connection and try again.';
  return msg;
}

// ---- Password fields (default path) ----

function PasswordForm({ email, setEmail, onConnected, onUseCode }: {
  email: string;
  setEmail: (v: string) => void;
  onConnected: (devices: RoborockDevice[]) => void;
  onUseCode: () => void;
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
      onConnected(await roborockLogin(email.trim(), password));
    } catch (err) {
      setError(friendlyError(err, 'Sign-in failed'));
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div>
        <label style={fieldLabelStyle}>Roborock account email</label>
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
            placeholder="Roborock app password"
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

      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginTop: 4 }}>
        <button type="button" style={switchLinkStyle} onClick={onUseCode}>
          Use an email code instead
        </button>
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
        Sign in with your Roborock app account. Control runs on your local network; the cloud is used once to pair.
      </div>
    </form>
  );
}

// ---- Email-code path (alternative) ----

function CodeForm({ email, setEmail, onConnected, onUsePassword }: {
  email: string;
  setEmail: (v: string) => void;
  onConnected: (devices: RoborockDevice[]) => void;
  onUsePassword: () => void;
}) {
  const [code, setCode] = useState('');
  const [codeSent, setCodeSent] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function sendCode(e: React.FormEvent) {
    e.preventDefault();
    if (!email.trim()) return;
    setLoading(true);
    setError(null);
    try {
      await roborockRequestCode(email.trim());
      setCodeSent(true);
    } catch (err) {
      setError(friendlyError(err, 'Could not send the code'));
    } finally {
      setLoading(false);
    }
  }

  async function verifyCode(e: React.FormEvent) {
    e.preventDefault();
    if (!code.trim()) return;
    setLoading(true);
    setError(null);
    try {
      onConnected(await roborockCodeLogin(email.trim(), code.trim()));
    } catch (err) {
      setError(friendlyError(err, 'Verification failed'));
    } finally {
      setLoading(false);
    }
  }

  if (!codeSent) {
    return (
      <form onSubmit={sendCode} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        <div>
          <label style={fieldLabelStyle}>Roborock account email</label>
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

        {error && <ErrorBanner message={error} />}

        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginTop: 4 }}>
          <button type="button" style={switchLinkStyle} onClick={onUsePassword}>
            Use a password instead
          </button>
          <button
            type="submit"
            className="btn-primary"
            disabled={loading || !email.trim()}
            style={{ opacity: loading || !email.trim() ? 0.5 : 1 }}
          >
            {loading ? 'Sending…' : 'Send code'}
          </button>
        </div>

        <div style={{ fontSize: 12, color: 'var(--text-muted)', textAlign: 'center' }}>
          Roborock will email a one-time code to verify your account.
        </div>
      </form>
    );
  }

  return (
    <form onSubmit={verifyCode} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div style={{
        fontSize: 13, color: 'var(--text-muted)', background: 'var(--bg-hover)',
        borderRadius: 'var(--radius-sm)', padding: '8px 12px',
      }}>
        Code sent to <span style={{ color: 'var(--text-secondary)' }}>{email.trim()}</span>
      </div>

      <div>
        <label style={fieldLabelStyle}>One-time code</label>
        <input
          type="text"
          value={code}
          onChange={e => setCode(e.target.value)}
          placeholder="Code from email"
          style={{ ...inputStyle, letterSpacing: '0.1em' }}
          onFocus={handleFocus}
          onBlur={handleBlur}
          autoComplete="one-time-code"
          inputMode="numeric"
          autoFocus
          required
        />
      </div>

      {error && <ErrorBanner message={error} />}

      <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 4 }}>
        <button type="button" className="btn-secondary" onClick={() => { setCodeSent(false); setCode(''); setError(null); }} disabled={loading}>
          Back
        </button>
        <button
          type="submit"
          className="btn-primary"
          disabled={loading || !code.trim()}
          style={{ opacity: loading || !code.trim() ? 0.5 : 1 }}
        >
          {loading ? 'Connecting…' : 'Connect'}
        </button>
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
          Roborock connected
        </div>
        <div style={{ fontSize: 13, color: 'var(--text-muted)', lineHeight: 1.6, maxWidth: 300 }}>
          Found {deviceCount} vacuum{deviceCount !== 1 ? 's' : ''} — {deviceCount !== 1 ? "they're" : "it's"} now in Setup.
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

interface RoborockOnboardingWizardProps {
  onClose: () => void;
  onSuccess: () => void;
}

export function RoborockOnboardingWizard({ onClose, onSuccess }: RoborockOnboardingWizardProps) {
  const navigate = useNavigate();
  const [mode, setMode] = useState<'password' | 'code'>('password');
  const [email, setEmail] = useState('');
  const [devices, setDevices] = useState<RoborockDevice[] | null>(null);

  function handleConnected(found: RoborockDevice[]) {
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
            <span style={{ fontSize: 22, lineHeight: 1 }}>🧹</span>
            <span style={{
              fontFamily: 'var(--font-heading)', fontSize: 17, fontWeight: 700,
              color: 'var(--text-primary)',
            }}>
              Connect Roborock
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
        ) : mode === 'password' ? (
          <PasswordForm email={email} setEmail={setEmail} onConnected={handleConnected} onUseCode={() => setMode('code')} />
        ) : (
          <CodeForm email={email} setEmail={setEmail} onConnected={handleConnected} onUsePassword={() => setMode('password')} />
        )}
      </div>
    </div>
  );
}
