import React, { useState } from 'react';
import { dysonBeginAuth, dysonVerifyAuth, dysonSaveDevices } from '../api/client';
import type { DysonDevice } from '../types';

// ---- Design tokens (from global.css) ----
// --accent-primary: #D4A054 (amber)
// --bg-surface, --bg-elevated, --bg-hover
// --border-default, --border-hover
// --text-primary, --text-secondary, --text-muted
// --radius-sm, --radius-md, --radius-lg
// --font-heading, --font-body

// ---- Step progress bar ----

interface StepBarProps {
  current: number; // 1–3
  total: number;
}

function StepBar({ current, total }: StepBarProps) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 0, marginBottom: 28 }}>
      {Array.from({ length: total }, (_, i) => {
        const step = i + 1;
        const done = step < current;
        const active = step === current;
        return (
          <React.Fragment key={step}>
            {/* Connector line before each step except first */}
            {i > 0 && (
              <div style={{
                flex: 1,
                height: 1,
                background: done || active
                  ? 'var(--accent-primary)'
                  : 'var(--border-default)',
                transition: 'background 0.3s',
              }} />
            )}
            {/* Step dot */}
            <div style={{
              position: 'relative',
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              gap: 6,
            }}>
              <div style={{
                width: 28,
                height: 28,
                borderRadius: '50%',
                background: done
                  ? 'var(--accent-primary)'
                  : active
                    ? 'var(--accent-primary-dim)'
                    : 'var(--bg-hover)',
                border: active
                  ? '2px solid var(--accent-primary)'
                  : done
                    ? '2px solid var(--accent-primary)'
                    : '2px solid var(--border-default)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                transition: 'background 0.3s, border-color 0.3s',
                flexShrink: 0,
              }}>
                {done ? (
                  <svg width="13" height="10" viewBox="0 0 13 10" fill="none">
                    <path d="M1 5L5 9L12 1" stroke="#0A0B0E" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
                  </svg>
                ) : (
                  <span style={{
                    fontSize: 11,
                    fontWeight: 700,
                    color: active ? 'var(--accent-primary)' : 'var(--text-muted)',
                    fontFamily: 'var(--font-heading)',
                  }}>{step}</span>
                )}
              </div>
            </div>
          </React.Fragment>
        );
      })}
    </div>
  );
}

// ---- Step labels ----

const STEP_LABELS = ['Account', 'Verify', 'Devices'];

function StepLabel({ current }: { current: number }) {
  return (
    <div style={{
      fontSize: 11,
      fontWeight: 600,
      color: 'var(--text-muted)',
      textTransform: 'uppercase',
      letterSpacing: '0.08em',
      marginTop: -20,
      marginBottom: 24,
      textAlign: 'center',
    }}>
      {STEP_LABELS[current - 1]}
    </div>
  );
}

// ---- Shared field styles ----

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

function handleFocus(e: React.FocusEvent<HTMLInputElement | HTMLSelectElement>) {
  e.currentTarget.style.borderColor = 'var(--accent-primary)';
  e.currentTarget.style.boxShadow = '0 0 0 3px color-mix(in srgb, var(--accent-primary) 20%, transparent)';
}

function handleBlur(e: React.FocusEvent<HTMLInputElement | HTMLSelectElement>) {
  e.currentTarget.style.borderColor = 'var(--border-default)';
  e.currentTarget.style.boxShadow = 'none';
}

// ---- Error banner ----

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

// ---- Dyson regions ----

const REGIONS = [
  { value: 'EU', label: 'Europe' },
  { value: 'US', label: 'United States' },
  { value: 'CN', label: 'China' },
  { value: 'AU', label: 'Australia' },
  { value: 'APAC', label: 'Asia Pacific' },
  { value: 'UK', label: 'United Kingdom' },
];

// ---- Step 1: Region + Email ----

interface Step1Props {
  onNext: (region: string, email: string, challengeId: string) => void;
}

function Step1({ onNext }: Step1Props) {
  const [region, setRegion] = useState('EU');
  const [email, setEmail] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!email.trim()) return;
    setLoading(true);
    setError(null);
    try {
      const { challengeId } = await dysonBeginAuth(region, email.trim());
      onNext(region, email.trim(), challengeId);
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Failed to contact Dyson cloud';
      if (msg.startsWith('502')) {
        setError('Dyson cloud is unreachable. Check your network connection and try again.');
      } else {
        setError(msg);
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div>
        <label style={fieldLabelStyle}>Region</label>
        <select
          value={region}
          onChange={e => setRegion(e.target.value)}
          onFocus={handleFocus}
          onBlur={handleBlur}
          style={{ ...inputStyle, appearance: 'none' as const }}
        >
          {REGIONS.map(r => (
            <option key={r.value} value={r.value}>{r.label}</option>
          ))}
        </select>
      </div>

      <div>
        <label style={fieldLabelStyle}>Dyson Account Email</label>
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

      <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 4 }}>
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
        Dyson will email a one-time code to verify your account.
      </div>
    </form>
  );
}

// ---- Step 2: Password + OTP ----

interface Step2Props {
  region: string;
  email: string;
  challengeId: string;
  onNext: (devices: DysonDevice[]) => void;
  onBack: () => void;
}

function Step2({ region, email, challengeId, onNext, onBack }: Step2Props) {
  const [password, setPassword] = useState('');
  const [otp, setOtp] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!password || !otp.trim()) return;
    setLoading(true);
    setError(null);
    try {
      const devices = await dysonVerifyAuth({ region, email, password, challengeId, otp: otp.trim() });
      onNext(devices);
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Verification failed';
      if (msg.startsWith('502')) {
        setError('Dyson cloud is unreachable. Check your credentials and try again.');
      } else if (msg.startsWith('401') || msg.toLowerCase().includes('unauthorized')) {
        setError('Wrong password or code. Check your credentials and the email code.');
      } else {
        setError(msg);
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div style={{
        fontSize: 13,
        color: 'var(--text-muted)',
        background: 'var(--bg-hover)',
        borderRadius: 'var(--radius-sm)',
        padding: '8px 12px',
      }}>
        Code sent to <span style={{ color: 'var(--text-secondary)' }}>{email}</span>
      </div>

      <div>
        <label style={fieldLabelStyle}>Password</label>
        <div style={{ position: 'relative' }}>
          <input
            type={showPassword ? 'text' : 'password'}
            value={password}
            onChange={e => setPassword(e.target.value)}
            placeholder="Dyson account password"
            style={{ ...inputStyle, paddingRight: 54 }}
            onFocus={handleFocus}
            onBlur={handleBlur}
            autoComplete="current-password"
            autoFocus
            required
          />
          <button
            type="button"
            onClick={() => setShowPassword(v => !v)}
            style={{
              position: 'absolute',
              right: 10,
              top: '50%',
              transform: 'translateY(-50%)',
              background: 'none',
              border: 'none',
              cursor: 'pointer',
              fontSize: 12,
              color: 'var(--text-muted)',
              padding: '0 4px',
              fontFamily: 'var(--font-body)',
            }}
          >
            {showPassword ? 'hide' : 'show'}
          </button>
        </div>
      </div>

      <div>
        <label style={fieldLabelStyle}>One-time code</label>
        <input
          type="text"
          value={otp}
          onChange={e => setOtp(e.target.value)}
          placeholder="6-digit code from email"
          style={{ ...inputStyle, letterSpacing: '0.1em' }}
          onFocus={handleFocus}
          onBlur={handleBlur}
          autoComplete="one-time-code"
          inputMode="numeric"
          maxLength={8}
          required
        />
      </div>

      {error && <ErrorBanner message={error} />}

      <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 4 }}>
        <button type="button" className="btn-secondary" onClick={onBack} disabled={loading}>
          Back
        </button>
        <button
          type="submit"
          className="btn-primary"
          disabled={loading || !password || !otp.trim()}
          style={{ opacity: loading || !password || !otp.trim() ? 0.5 : 1 }}
        >
          {loading ? 'Verifying…' : 'Verify'}
        </button>
      </div>
    </form>
  );
}

// ---- Step 3: Select devices + optional IP ----

interface DeviceRowState {
  selected: boolean;
  ip: string;
}

interface Step3Props {
  devices: DysonDevice[];
  onDone: () => void;
  onBack: () => void;
}

function Step3({ devices, onDone, onBack }: Step3Props) {
  const [rows, setRows] = useState<Record<string, DeviceRowState>>(() => {
    const init: Record<string, DeviceRowState> = {};
    for (const d of devices) {
      init[d.serial] = { selected: true, ip: d.ip ?? '' };
    }
    return init;
  });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const selectedDevices = devices.filter(d => rows[d.serial]?.selected);

  async function handleSave() {
    if (selectedDevices.length === 0) {
      setError('Select at least one device to add.');
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const payload: DysonDevice[] = selectedDevices.map(d => ({
        serial: d.serial,
        productType: d.productType,
        name: d.name,
        mqttPassword: d.mqttPassword,
        variant: d.variant,
        ip: rows[d.serial]?.ip?.trim() || null,
      }));
      await dysonSaveDevices(payload);
      onDone();
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Failed to save devices';
      if (msg.startsWith('400')) {
        setError('No devices were provided. Select at least one device.');
      } else {
        setError(msg);
      }
    } finally {
      setLoading(false);
    }
  }

  function toggleDevice(serial: string) {
    setRows(prev => ({
      ...prev,
      [serial]: { ...prev[serial], selected: !prev[serial].selected },
    }));
  }

  function setIp(serial: string, ip: string) {
    setRows(prev => ({
      ...prev,
      [serial]: { ...prev[serial], ip },
    }));
  }

  if (devices.length === 0) {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        <div style={{ fontSize: 13, color: 'var(--text-muted)', textAlign: 'center', padding: '16px 0' }}>
          No Dyson devices found on this account.
        </div>
        <div style={{ display: 'flex', justifyContent: 'flex-start' }}>
          <button type="button" className="btn-secondary" onClick={onBack}>Back</button>
        </div>
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div style={{ fontSize: 13, color: 'var(--text-muted)' }}>
        {devices.length} device{devices.length !== 1 ? 's' : ''} found. Select which to add to Vidar.
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 10, maxHeight: 280, overflowY: 'auto' }}>
        {devices.map(device => {
          const row = rows[device.serial];
          const isSelected = row?.selected ?? false;
          return (
            <div
              key={device.serial}
              style={{
                background: isSelected ? 'color-mix(in srgb, var(--accent-primary) 8%, var(--bg-hover))' : 'var(--bg-hover)',
                border: isSelected
                  ? '1px solid color-mix(in srgb, var(--accent-primary) 40%, transparent)'
                  : '1px solid var(--border-default)',
                borderRadius: 'var(--radius-sm)',
                padding: '12px 14px',
                transition: 'background 0.15s, border-color 0.15s',
              }}
            >
              {/* Device header row */}
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                {/* Custom checkbox */}
                <button
                  type="button"
                  role="checkbox"
                  aria-checked={isSelected}
                  onClick={() => toggleDevice(device.serial)}
                  style={{
                    width: 18,
                    height: 18,
                    borderRadius: 4,
                    border: isSelected
                      ? '2px solid var(--accent-primary)'
                      : '2px solid var(--border-default)',
                    background: isSelected ? 'var(--accent-primary)' : 'var(--bg-elevated)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    flexShrink: 0,
                    padding: 0,
                    cursor: 'pointer',
                    transition: 'background 0.15s, border-color 0.15s',
                  }}
                >
                  {isSelected && (
                    <svg width="10" height="8" viewBox="0 0 10 8" fill="none">
                      <path d="M1 4L4 7L9 1" stroke="#0A0B0E" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
                    </svg>
                  )}
                </button>

                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{
                    fontSize: 14,
                    fontWeight: 600,
                    color: 'var(--text-primary)',
                    whiteSpace: 'nowrap',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                  }}>
                    {device.name}
                  </div>
                  <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 2 }}>
                    {device.productType}{device.variant ? ` · ${device.variant}` : ''} · {device.serial}
                  </div>
                </div>
              </div>

              {/* IP input (shown when selected) */}
              {isSelected && (
                <div style={{ marginTop: 10 }}>
                  <label style={{ ...fieldLabelStyle, marginBottom: 4 }}>
                    Local IP <span style={{ fontWeight: 400, textTransform: 'none', letterSpacing: 0 }}>— optional, for LAN control</span>
                  </label>
                  <input
                    type="text"
                    value={row?.ip ?? ''}
                    onChange={e => setIp(device.serial, e.target.value)}
                    placeholder="192.168.1.x"
                    style={{ ...inputStyle, fontSize: 13 }}
                    onFocus={handleFocus}
                    onBlur={handleBlur}
                    autoComplete="off"
                  />
                </div>
              )}
            </div>
          );
        })}
      </div>

      {error && <ErrorBanner message={error} />}

      <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 4 }}>
        <button type="button" className="btn-secondary" onClick={onBack} disabled={loading}>
          Back
        </button>
        <button
          type="button"
          className="btn-primary"
          onClick={handleSave}
          disabled={loading || selectedDevices.length === 0}
          style={{ opacity: loading || selectedDevices.length === 0 ? 0.5 : 1 }}
        >
          {loading ? 'Saving…' : `Add ${selectedDevices.length} device${selectedDevices.length !== 1 ? 's' : ''}`}
        </button>
      </div>
    </div>
  );
}

// ---- Success state ----

function SuccessView({ deviceCount, onClose }: { deviceCount: number; onClose: () => void }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 20, padding: '8px 0' }}>
      <div style={{
        width: 56,
        height: 56,
        borderRadius: '50%',
        background: 'color-mix(in srgb, var(--accent-green) 15%, transparent)',
        border: '2px solid color-mix(in srgb, var(--accent-green) 40%, transparent)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
      }}>
        <svg width="24" height="19" viewBox="0 0 24 19" fill="none">
          <path d="M2 10L9 17L22 2" stroke="var(--accent-green)" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </div>

      <div style={{ textAlign: 'center' }}>
        <div style={{
          fontFamily: 'var(--font-heading)',
          fontSize: 18,
          fontWeight: 700,
          color: 'var(--text-primary)',
          marginBottom: 6,
        }}>
          Dyson connected
        </div>
        <div style={{ fontSize: 13, color: 'var(--text-muted)', lineHeight: 1.5 }}>
          {deviceCount} device{deviceCount !== 1 ? 's' : ''} added. The Dyson integration
          is now active — devices will appear in Vidar shortly.
        </div>
      </div>

      <button type="button" className="btn-primary" onClick={onClose} style={{ minWidth: 120 }}>
        Done
      </button>
    </div>
  );
}

// ---- Main wizard ----

interface DysonOnboardingWizardProps {
  onClose: () => void;
  onSuccess: () => void;
}

export function DysonOnboardingWizard({ onClose, onSuccess }: DysonOnboardingWizardProps) {
  const [step, setStep] = useState<1 | 2 | 3 | 'done'>(1);

  // Step 1 output
  const [region, setRegion] = useState('');
  const [email, setEmail] = useState('');
  const [challengeId, setChallengeId] = useState('');

  // Step 2 output
  const [devices, setDevices] = useState<DysonDevice[]>([]);

  function handleStep1Done(r: string, e: string, cid: string) {
    setRegion(r);
    setEmail(e);
    setChallengeId(cid);
    setStep(2);
  }

  function handleStep2Done(found: DysonDevice[]) {
    setDevices(found);
    setStep(3);
  }

  function handleStep3Done() {
    setStep('done');
    onSuccess();
  }

  const stepNumber = step === 'done' ? 3 : step;

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
              fontFamily: 'var(--font-heading)',
              fontSize: 17,
              fontWeight: 700,
              color: 'var(--text-primary)',
            }}>
              Connect Dyson
            </span>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            style={{
              background: 'none',
              border: 'none',
              padding: '4px 6px',
              cursor: 'pointer',
              color: 'var(--text-muted)',
              fontSize: 18,
              lineHeight: 1,
              borderRadius: 'var(--radius-sm)',
              transition: 'color 0.15s',
            }}
            onMouseEnter={e => (e.currentTarget.style.color = 'var(--text-primary)')}
            onMouseLeave={e => (e.currentTarget.style.color = 'var(--text-muted)')}
          >
            ✕
          </button>
        </div>

        {/* Step progress */}
        {step !== 'done' && (
          <>
            <StepBar current={stepNumber} total={3} />
            <StepLabel current={stepNumber} />
          </>
        )}

        {/* Step content */}
        {step === 1 && (
          <Step1 onNext={handleStep1Done} />
        )}
        {step === 2 && (
          <Step2
            region={region}
            email={email}
            challengeId={challengeId}
            onNext={handleStep2Done}
            onBack={() => setStep(1)}
          />
        )}
        {step === 3 && (
          <Step3
            devices={devices}
            onDone={handleStep3Done}
            onBack={() => setStep(2)}
          />
        )}
        {step === 'done' && (
          <SuccessView deviceCount={devices.length} onClose={onClose} />
        )}
      </div>
    </div>
  );
}
