import React, { useEffect, useState } from 'react';
import type { IntegrationConfig } from '../api/client';
import { getIntegrations, saveIntegration } from '../api/client';

// ---- Types ----

interface FieldDef {
  key: string;
  label: string;
  placeholder?: string;
  type?: 'text' | 'password';
  defaultValue?: string;
}

interface IntegrationDef {
  id: string;
  name: string;
  icon: string;
  description: string;
  fields: FieldDef[];
}

const INTEGRATION_DEFS: IntegrationDef[] = [
  {
    id: 'unifi',
    name: 'UniFi',
    icon: '⬡',
    description: 'Ubiquiti UniFi network devices and clients',
    fields: [
      { key: 'host', label: 'Host / IP', placeholder: 'e.g. 192.168.1.1', type: 'text' },
      { key: 'apiKey', label: 'API Key', placeholder: 'UniFi API key', type: 'password' },
      { key: 'siteId', label: 'Site ID', placeholder: 'default', defaultValue: 'default', type: 'text' },
      { key: 'pollIntervalSeconds', label: 'Poll Interval (seconds)', placeholder: '30', defaultValue: '30', type: 'text' },
    ],
  },
];

// ---- Styles ----

const pageStyle: React.CSSProperties = {
  maxWidth: 720,
};

const headingStyle: React.CSSProperties = {
  fontFamily: 'var(--font-heading)',
  fontSize: 26,
  fontWeight: 700,
  color: 'var(--text-primary)',
  letterSpacing: '-0.02em',
  marginBottom: 6,
};

const subtitleStyle: React.CSSProperties = {
  fontSize: 13,
  color: 'var(--text-muted)',
  marginBottom: 32,
};

const cardStyle: React.CSSProperties = {
  background: 'var(--bg-elevated)',
  border: '1px solid var(--border-default)',
  borderRadius: 'var(--radius-md)',
  padding: '22px 24px',
  boxShadow: 'var(--shadow-card)',
  marginBottom: 16,
};

const cardHeaderStyle: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 12,
  marginBottom: 6,
};

const cardTitleStyle: React.CSSProperties = {
  fontFamily: 'var(--font-heading)',
  fontSize: 17,
  fontWeight: 700,
  color: 'var(--text-primary)',
  flex: 1,
};

const cardDescStyle: React.CSSProperties = {
  fontSize: 13,
  color: 'var(--text-muted)',
  marginBottom: 22,
};

const fieldLabelStyle: React.CSSProperties = {
  fontSize: 11,
  fontWeight: 600,
  color: 'var(--text-muted)',
  textTransform: 'uppercase',
  letterSpacing: '0.06em',
  display: 'block',
  marginBottom: 5,
};

const fieldInputStyle: React.CSSProperties = {
  background: 'var(--bg-surface)',
  border: '1px solid var(--border-default)',
  borderRadius: 'var(--radius-sm)',
  padding: '9px 13px',
  color: 'var(--text-primary)',
  fontFamily: 'var(--font-body)',
  fontSize: 14,
  outline: 'none',
  transition: 'border-color 0.15s, box-shadow 0.15s',
  width: '100%',
  boxSizing: 'border-box' as const,
};

const dividerStyle: React.CSSProperties = {
  borderTop: '1px solid var(--border-subtle)',
  margin: '20px 0',
};

const actionsRowStyle: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 10,
  marginTop: 4,
};

// ---- Helpers ----

function handleFocus(e: React.FocusEvent<HTMLInputElement>) {
  e.currentTarget.style.borderColor = 'var(--accent-primary)';
  e.currentTarget.style.boxShadow = '0 0 0 3px color-mix(in srgb, var(--accent-primary) 20%, transparent)';
}

function handleBlur(e: React.FocusEvent<HTMLInputElement>) {
  e.currentTarget.style.borderColor = 'var(--border-default)';
  e.currentTarget.style.boxShadow = 'none';
}

// ---- Toggle Switch (inline, no dep) ----

function ToggleSwitch({ checked, onChange }: { checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      onClick={() => onChange(!checked)}
      style={{
        position: 'relative',
        display: 'inline-flex',
        alignItems: 'center',
        width: 42,
        height: 24,
        borderRadius: 12,
        border: 'none',
        cursor: 'pointer',
        padding: 0,
        flexShrink: 0,
        background: checked ? 'var(--accent-primary)' : 'var(--border-default)',
        transition: 'background 0.2s',
      }}
    >
      <span style={{
        position: 'absolute',
        left: checked ? 20 : 2,
        width: 20,
        height: 20,
        borderRadius: '50%',
        background: 'white',
        transition: 'left 0.2s',
        boxShadow: '0 1px 3px rgba(0,0,0,0.3)',
      }} />
    </button>
  );
}

// ---- IntegrationCard ----

interface IntegrationCardProps {
  def: IntegrationDef;
  config: IntegrationConfig | undefined;
  onSaved: () => void;
}

function IntegrationCard({ def, config, onSaved }: IntegrationCardProps) {
  const [enabled, setEnabled] = useState(config?.enabled ?? false);
  const [settings, setSettings] = useState<Record<string, string>>(() => {
    const defaults: Record<string, string> = {};
    for (const f of def.fields) {
      defaults[f.key] = config?.settings?.[f.key] ?? f.defaultValue ?? '';
    }
    return defaults;
  });
  const [showPassword, setShowPassword] = useState<Record<string, boolean>>({});
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  // Sync with loaded config
  useEffect(() => {
    if (config) {
      setEnabled(config.enabled);
      const next: Record<string, string> = {};
      for (const f of def.fields) {
        next[f.key] = config.settings?.[f.key] ?? f.defaultValue ?? '';
      }
      setSettings(next);
    }
  }, [config, def.fields]);

  async function handleSave() {
    setSaving(true);
    setSaveError(null);
    setSaved(false);
    try {
      await saveIntegration(def.id, { enabled, settings });
      setSaved(true);
      onSaved();
      setTimeout(() => setSaved(false), 2000);
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : 'Failed to save');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div style={cardStyle}>
      {/* Header */}
      <div style={cardHeaderStyle}>
        <span style={{ fontSize: 22, lineHeight: 1 }}>{def.icon}</span>
        <div style={cardTitleStyle}>{def.name}</div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span style={{ fontSize: 12, color: enabled ? 'var(--accent-primary)' : 'var(--text-muted)', fontWeight: 500 }}>
            {enabled ? 'Enabled' : 'Disabled'}
          </span>
          <ToggleSwitch checked={enabled} onChange={setEnabled} />
        </div>
      </div>

      <div style={cardDescStyle}>{def.description}</div>

      <div style={dividerStyle} />

      {/* Settings fields */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 16, marginBottom: 20 }}>
        {def.fields.map(field => {
          const isPassword = field.type === 'password';
          const visible = showPassword[field.key] ?? false;
          return (
            <div key={field.key}>
              <label style={fieldLabelStyle}>{field.label}</label>
              <div style={{ position: 'relative' }}>
                <input
                  type={isPassword && !visible ? 'password' : 'text'}
                  value={settings[field.key] ?? ''}
                  onChange={e => setSettings(prev => ({ ...prev, [field.key]: e.target.value }))}
                  placeholder={field.placeholder}
                  style={fieldInputStyle}
                  onFocus={handleFocus}
                  onBlur={handleBlur}
                  autoComplete={isPassword ? 'new-password' : 'off'}
                />
                {isPassword && (
                  <button
                    type="button"
                    onClick={() => setShowPassword(prev => ({ ...prev, [field.key]: !visible }))}
                    style={{
                      position: 'absolute',
                      right: 10,
                      top: '50%',
                      transform: 'translateY(-50%)',
                      background: 'none',
                      border: 'none',
                      cursor: 'pointer',
                      fontSize: 13,
                      color: 'var(--text-muted)',
                      padding: '0 4px',
                      fontFamily: 'var(--font-body)',
                    }}
                  >
                    {visible ? 'hide' : 'show'}
                  </button>
                )}
              </div>
            </div>
          );
        })}
      </div>

      {/* Error / Success */}
      {saveError && (
        <div style={{ fontSize: 13, color: 'var(--accent-red)', marginBottom: 12 }}>{saveError}</div>
      )}

      {/* Actions */}
      <div style={actionsRowStyle}>
        <button
          className="btn-primary"
          disabled={saving}
          style={{ opacity: saving ? 0.6 : 1 }}
          onClick={handleSave}
        >
          {saving ? 'Saving…' : 'Save'}
        </button>
        {saved && (
          <span style={{ fontSize: 13, color: 'var(--accent-primary)', fontWeight: 500 }}>
            Saved
          </span>
        )}
      </div>
    </div>
  );
}

// ---- IntegrationsPage ----

export function IntegrationsPage() {
  const [configs, setConfigs] = useState<IntegrationConfig[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    try {
      const data = await getIntegrations();
      setConfigs(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load integrations');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  if (loading) {
    return (
      <div className="page-content" style={{ color: 'var(--text-muted)', fontFamily: 'var(--font-body)' }}>
        Loading integrations…
      </div>
    );
  }

  const configMap = Object.fromEntries(configs.map(c => [c.id, c]));

  return (
    <div className="page-content" style={pageStyle}>
      <div style={headingStyle}>Integrations</div>
      <div style={subtitleStyle}>Configure communication bridges and external systems.</div>

      {error && (
        <div style={{
          background: 'color-mix(in srgb, var(--accent-red) 12%, transparent)',
          border: '1px solid color-mix(in srgb, var(--accent-red) 35%, transparent)',
          borderRadius: 'var(--radius-sm)', padding: '10px 14px', marginBottom: 20,
          fontSize: 13, color: 'var(--accent-red)',
        }}>
          {error}
        </div>
      )}

      {INTEGRATION_DEFS.map(def => (
        <IntegrationCard
          key={def.id}
          def={def}
          config={configMap[def.id]}
          onSaved={load}
        />
      ))}
    </div>
  );
}
