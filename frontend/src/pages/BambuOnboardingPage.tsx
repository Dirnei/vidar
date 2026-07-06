import React, { useEffect, useRef, useState } from 'react';
import { bambuAddPrinter, bambuDeletePrinter, bambuListPrinters } from '../api/client';
import type { BambuPrinter } from '../types';

// Bambu printers have no cloud account to sign into — LAN-only mode means each
// printer is added by copying three values straight off its own touchscreen
// (Settings > Network > LAN Only Mode): the IP, the serial, and the access code.
// So unlike the Dyson/Roborock step-wizards (one OAuth flow, then done), this is a
// standing roster: a list of already-added printers plus a form to add the next
// one. The wizard stays open across adds so setting up several printers in a row
// doesn't mean reopening the modal each time.

// ---- Shared field styles (same visual language as Dyson/Roborock wizards) ----

const fieldLabelStyle: React.CSSProperties = {
  fontSize: 11,
  fontWeight: 600,
  color: 'var(--text-muted)',
  textTransform: 'uppercase',
  letterSpacing: '0.06em',
  display: 'block',
  marginBottom: 5,
};

const sectionLabelStyle: React.CSSProperties = {
  fontSize: 11,
  fontWeight: 700,
  color: 'var(--text-muted)',
  textTransform: 'uppercase',
  letterSpacing: '0.1em',
  marginBottom: 10,
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

// Identifiers (serial, access code, host) are read directly off the printer's
// own screen — set them in monospace so they read the way they do there,
// distinct from the free-text "name" field.
const monoInputStyle: React.CSSProperties = {
  ...inputStyle,
  fontFamily: 'var(--font-mono, ui-monospace, SFMono-Regular, Menlo, monospace)',
  letterSpacing: '0.03em',
};

function handleFocus(e: React.FocusEvent<HTMLInputElement>) {
  e.currentTarget.style.borderColor = 'var(--accent-primary)';
  e.currentTarget.style.boxShadow = '0 0 0 3px color-mix(in srgb, var(--accent-primary) 20%, transparent)';
}

function handleBlur(e: React.FocusEvent<HTMLInputElement>) {
  e.currentTarget.style.borderColor = 'var(--border-default)';
  e.currentTarget.style.boxShadow = 'none';
}

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
  if (msg.startsWith('502')) return 'Could not reach the printer bridge. Check your connection and try again.';
  if (msg.startsWith('400')) return 'Check the IP, serial and access code and try again.';
  return msg;
}

// ---- Printer roster row ----

function PrinterRow({ printer, onDelete }: { printer: BambuPrinter; onDelete: (serial: string) => void }) {
  const [deleting, setDeleting] = useState(false);
  const [hover, setHover] = useState(false);
  const [focused, setFocused] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleDelete() {
    setDeleting(true);
    setError(null);
    try {
      await bambuDeletePrinter(printer.serial);
      onDelete(printer.serial);
    } catch (err) {
      setError(friendlyError(err, 'Could not remove the printer'));
    } finally {
      setDeleting(false);
    }
  }

  return (
    <div
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 6,
        padding: '10px 12px',
        borderBottom: '1px solid var(--border-default)',
        transition: 'background 0.15s',
        background: hover ? 'var(--bg-hover)' : 'transparent',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        <div style={{
          width: 3,
          alignSelf: 'stretch',
          borderRadius: 2,
          background: hover ? 'var(--accent-primary)' : 'transparent',
          transition: 'background 0.15s',
          flexShrink: 0,
        }} />
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{
            fontSize: 14,
            fontWeight: 600,
            color: 'var(--text-primary)',
            fontFamily: 'var(--font-body)',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
          }}>
            {printer.name || printer.serial}
          </div>
          <div style={{
            fontSize: 12,
            color: 'var(--text-muted)',
            fontFamily: 'var(--font-mono, ui-monospace, SFMono-Regular, Menlo, monospace)',
            letterSpacing: '0.02em',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
          }}>
            {printer.host} · {printer.serial}{printer.model ? ` · ${printer.model}` : ''}
          </div>
        </div>
        <button
          type="button"
          onClick={handleDelete}
          disabled={deleting}
          onFocus={() => setFocused(true)}
          onBlur={() => setFocused(false)}
          aria-label={`Remove ${printer.name || printer.serial}`}
          style={{
            background: 'none',
            border: 'none',
            cursor: deleting ? 'default' : 'pointer',
            padding: '4px 6px',
            fontSize: 14,
            lineHeight: 1,
            color: hover ? 'var(--accent-red)' : 'var(--text-muted)',
            opacity: hover || focused || deleting ? 1 : 0,
            transition: 'opacity 0.15s, color 0.15s',
            flexShrink: 0,
          }}
        >
          {deleting ? '…' : '🗑'}
        </button>
      </div>
      {error && <ErrorBanner message={error} />}
    </div>
  );
}

function PrinterRoster({ printers, onDelete }: { printers: BambuPrinter[]; onDelete: (serial: string) => void }) {
  if (printers.length === 0) {
    return (
      <div style={{
        border: '1px dashed var(--border-default)',
        borderRadius: 'var(--radius-sm)',
        padding: '16px 14px',
        fontSize: 13,
        color: 'var(--text-muted)',
        textAlign: 'center',
      }}>
        No printers yet — add one below using the details from its network screen.
      </div>
    );
  }

  return (
    <div style={{
      border: '1px solid var(--border-default)',
      borderRadius: 'var(--radius-sm)',
      overflow: 'hidden',
    }}>
      {printers.map((p, i) => (
        <div key={p.serial} style={i === printers.length - 1 ? { borderBottom: 'none' } : undefined}>
          <PrinterRow printer={p} onDelete={onDelete} />
        </div>
      ))}
    </div>
  );
}

// ---- Add-printer form ----

interface AddFormProps {
  onAdded: (printer: BambuPrinter) => void;
}

function AddPrinterForm({ onAdded }: AddFormProps) {
  const [name, setName] = useState('');
  const [host, setHost] = useState('');
  const [serial, setSerial] = useState('');
  const [accessCode, setAccessCode] = useState('');
  const [showCode, setShowCode] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [justAdded, setJustAdded] = useState(false);
  const justAddedTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    return () => {
      if (justAddedTimeoutRef.current !== null) clearTimeout(justAddedTimeoutRef.current);
    };
  }, []);

  const ready = name.trim() !== '' && host.trim() !== '' && serial.trim() !== '' && accessCode.trim() !== '';

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!ready || loading) return;
    setLoading(true);
    setError(null);
    try {
      await bambuAddPrinter({
        host: host.trim(),
        serial: serial.trim(),
        accessCode: accessCode.trim(),
        name: name.trim(),
      });
      onAdded({ host: host.trim(), serial: serial.trim(), model: '', name: name.trim() });
      setName('');
      setHost('');
      setSerial('');
      setAccessCode('');
      setJustAdded(true);
      if (justAddedTimeoutRef.current !== null) clearTimeout(justAddedTimeoutRef.current);
      justAddedTimeoutRef.current = setTimeout(() => setJustAdded(false), 2000);
    } catch (err) {
      setError(friendlyError(err, 'Could not add the printer'));
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div>
        <label style={fieldLabelStyle}>Name</label>
        <input
          type="text"
          value={name}
          onChange={e => setName(e.target.value)}
          placeholder="e.g. Living Room X1C"
          style={inputStyle}
          onFocus={handleFocus}
          onBlur={handleBlur}
          autoFocus
          required
        />
      </div>

      <div style={{ display: 'flex', gap: 12 }}>
        <div style={{ flex: 1 }}>
          <label style={fieldLabelStyle}>Host / IP</label>
          <input
            type="text"
            value={host}
            onChange={e => setHost(e.target.value)}
            placeholder="192.168.1.50"
            style={monoInputStyle}
            onFocus={handleFocus}
            onBlur={handleBlur}
            autoComplete="off"
            required
          />
        </div>
        <div style={{ flex: 1 }}>
          <label style={fieldLabelStyle}>Serial</label>
          <input
            type="text"
            value={serial}
            onChange={e => setSerial(e.target.value)}
            placeholder="01S00A000000000"
            style={monoInputStyle}
            onFocus={handleFocus}
            onBlur={handleBlur}
            autoComplete="off"
            required
          />
        </div>
      </div>

      <div>
        <label style={fieldLabelStyle}>Access code</label>
        <div style={{ position: 'relative' }}>
          <input
            type={showCode ? 'text' : 'password'}
            value={accessCode}
            onChange={e => setAccessCode(e.target.value)}
            placeholder="8-digit code from the printer screen"
            style={{ ...monoInputStyle, paddingRight: 54 }}
            onFocus={handleFocus}
            onBlur={handleBlur}
            autoComplete="off"
            required
          />
          <button
            type="button"
            onClick={() => setShowCode(v => !v)}
            style={{
              position: 'absolute', right: 10, top: '50%', transform: 'translateY(-50%)',
              background: 'none', border: 'none', cursor: 'pointer', fontSize: 12,
              color: 'var(--text-muted)', padding: '0 4px', fontFamily: 'var(--font-body)',
            }}
          >
            {showCode ? 'hide' : 'show'}
          </button>
        </div>
      </div>

      {error && <ErrorBanner message={error} />}

      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 12, marginTop: 2 }}>
        {justAdded && (
          <span style={{ fontSize: 13, color: 'var(--accent-primary)', fontWeight: 500 }}>
            Added
          </span>
        )}
        <button
          type="submit"
          className="btn-primary"
          disabled={loading || !ready}
          style={{ opacity: loading || !ready ? 0.5 : 1 }}
        >
          {loading ? 'Adding…' : 'Add printer'}
        </button>
      </div>

      <div style={{ fontSize: 12, color: 'var(--text-muted)', textAlign: 'center' }}>
        Found on the printer: Settings → Network → LAN Only Mode.
      </div>
    </form>
  );
}

// ---- Main wizard ----

interface BambuOnboardingWizardProps {
  onClose: () => void;
  onSuccess: () => void;
}

export function BambuOnboardingWizard({ onClose, onSuccess }: BambuOnboardingWizardProps) {
  const [printers, setPrinters] = useState<BambuPrinter[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    bambuListPrinters()
      .then(list => { if (!cancelled) setPrinters(list); })
      .catch(err => { if (!cancelled) setLoadError(friendlyError(err, 'Could not load printers')); });
    return () => { cancelled = true; };
  }, []);

  function handleAdded(printer: BambuPrinter) {
    setPrinters(prev => {
      const withoutDup = (prev ?? []).filter(p => p.serial !== printer.serial);
      return [...withoutDup, printer];
    });
    onSuccess();
  }

  function handleDeleted(serial: string) {
    setPrinters(prev => (prev ?? []).filter(p => p.serial !== serial));
    onSuccess();
  }

  return (
    <div
      className="modal-overlay"
      onClick={e => e.target === e.currentTarget && onClose()}
    >
      <div
        className="modal-dialog"
        style={{
          width: 460,
          maxWidth: 'calc(100vw - 32px)',
          maxHeight: 'calc(100vh - 48px)',
          overflowY: 'auto',
        }}
      >
        {/* Header */}
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 24 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={{ fontSize: 22, lineHeight: 1 }}>🖨️</span>
            <span style={{
              fontFamily: 'var(--font-heading)', fontSize: 17, fontWeight: 700,
              color: 'var(--text-primary)',
            }}>
              Connect Bambu Lab
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

        {/* Roster */}
        <div style={{ marginBottom: 22 }}>
          <div style={sectionLabelStyle}>Printers</div>
          {loadError ? (
            <ErrorBanner message={loadError} />
          ) : (
            <PrinterRoster printers={printers ?? []} onDelete={handleDeleted} />
          )}
        </div>

        {/* Add form */}
        <div>
          <div style={sectionLabelStyle}>Add a printer</div>
          <AddPrinterForm onAdded={handleAdded} />
        </div>
      </div>
    </div>
  );
}
