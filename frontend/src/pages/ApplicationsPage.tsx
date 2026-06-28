import React, { useEffect, useState } from 'react';
import { getApplications, getWebhookRoutes, saveApplication, dysonGetAccount } from '../api/client';
import type { Application, WebhookRoute } from '../types';
import { DysonOnboardingWizard } from './DysonOnboardingPage';

// ---- Types ----

interface FieldDef {
  key: string;
  label: string;
  placeholder?: string;
  type?: 'text' | 'password';
  defaultValue?: string;
}

interface AppDef {
  id: string;
  icon: string;
  description: string;
  fields: FieldDef[];
}

const APP_DEFS: AppDef[] = [
  {
    id: 'shelly',
    icon: '\u{1F50C}',
    description: 'HTTP-polled Shelly smart home devices. Discovery is manual via IP on the Setup page.',
    fields: [],
  },
  {
    id: 'zigbee2mqtt',
    icon: '\u{1F4E1}',
    description: 'Zigbee devices via Zigbee2MQTT bridge and MQTT broker.',
    fields: [
      { key: 'mqttHost', label: 'MQTT Broker Host', placeholder: 'e.g. 10.220.220.10', type: 'text' },
      { key: 'mqttPort', label: 'MQTT Port', placeholder: '1883', defaultValue: '1883', type: 'text' },
      { key: 'mqttUser', label: 'MQTT Username', placeholder: 'optional', type: 'text' },
      { key: 'mqttPassword', label: 'MQTT Password', placeholder: 'optional', type: 'password' },
      { key: 'baseTopic', label: 'Base Topic', placeholder: 'zigbee2mqtt', defaultValue: 'zigbee2mqtt', type: 'text' },
    ],
  },
  {
    id: 'unifi',
    icon: '⬡',
    description: 'Ubiquiti UniFi network devices and clients.',
    fields: [
      { key: 'host', label: 'Host / IP', placeholder: 'e.g. 192.168.1.1', type: 'text' },
      { key: 'apiKey', label: 'API Key', placeholder: 'UniFi API key', type: 'password' },
      { key: 'siteId', label: 'Site ID', placeholder: 'default', defaultValue: 'default', type: 'text' },
      { key: 'pollIntervalSeconds', label: 'Poll Interval (seconds)', placeholder: '30', defaultValue: '30', type: 'text' },
    ],
  },
  {
    id: 'homeconnect',
    icon: '🏠',
    description: 'Bosch/Siemens home appliances via Home Connect cloud API.',
    fields: [
      { key: 'clientId', label: 'Client ID', placeholder: 'Home Connect developer client ID', type: 'text' },
      { key: 'clientSecret', label: 'Client Secret', placeholder: 'Home Connect client secret', type: 'password' },
      { key: 'oauthAuthorizeEndpoint', label: 'Authorize Endpoint', placeholder: 'https://api.home-connect.com/security/oauth/authorize', defaultValue: 'https://api.home-connect.com/security/oauth/authorize', type: 'text' },
      { key: 'oauthTokenEndpoint', label: 'Token Endpoint', placeholder: 'https://api.home-connect.com/security/oauth/token', defaultValue: 'https://api.home-connect.com/security/oauth/token', type: 'text' },
      { key: 'oauthScopes', label: 'Scopes', placeholder: 'IdentifyAppliance Monitor Settings Control', defaultValue: 'IdentifyAppliance Monitor Settings Control', type: 'text' },
      { key: 'hostBaseUrl', label: 'Vidar Host URL', placeholder: 'http://vidar-host:8080', defaultValue: 'http://vidar-host:8080', type: 'text' },
    ],
  },
  {
    id: 'e3dc',
    icon: '⚡',
    description: 'E3/DC S10 home energy storage system via RSCP protocol.',
    fields: [
      { key: 'host', label: 'E3DC IP Address', type: 'text' },
      { key: 'port', label: 'RSCP Port', type: 'text' },
      { key: 'user', label: 'E3DC Username', type: 'text' },
      { key: 'password', label: 'E3DC Password', type: 'password' },
      { key: 'rscpKey', label: 'RSCP Encryption Key', type: 'password' },
      { key: 'pollingInterval', label: 'Polling Interval (seconds)', type: 'text' },
    ],
  },
  {
    id: 'dyson',
    icon: '\u{1F300}',
    description: 'Dyson Connected devices via Dyson cloud account. Use the Connect wizard to authenticate and add your fans and purifiers.',
    fields: [],
  },
];

// ---- Status colors ----

function statusColor(status: Application['status']): string {
  switch (status) {
    case 'running': return 'var(--accent-green)';
    case 'stopped': return 'var(--text-muted)';
    case 'error': return 'var(--accent-red)';
    case 'unconfigured': return 'var(--text-muted)';
  }
}

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

const sectionLabelStyle: React.CSSProperties = {
  fontSize: 11,
  fontWeight: 600,
  color: 'var(--text-muted)',
  textTransform: 'uppercase',
  letterSpacing: '0.08em',
  marginBottom: 12,
  marginTop: 28,
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
  flexWrap: 'wrap',
};

const cardTitleStyle: React.CSSProperties = {
  fontFamily: 'var(--font-heading)',
  fontSize: 17,
  fontWeight: 700,
  color: 'var(--text-primary)',
};

const typeBadgeStyle: React.CSSProperties = {
  fontSize: 10,
  fontWeight: 600,
  textTransform: 'uppercase',
  letterSpacing: '0.06em',
  padding: '2px 8px',
  borderRadius: 'var(--radius-sm)',
  background: 'var(--accent-primary-dim)',
  color: 'var(--accent-primary)',
};

const statusDotStyle = (color: string): React.CSSProperties => ({
  width: 8,
  height: 8,
  borderRadius: '50%',
  background: color,
  flexShrink: 0,
});

const statusLabelStyle = (color: string): React.CSSProperties => ({
  fontSize: 12,
  fontWeight: 500,
  color,
});

const deviceCountStyle: React.CSSProperties = {
  fontSize: 12,
  color: 'var(--text-muted)',
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

const actionsRowStyle: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 10,
  marginTop: 4,
};

const dysonLinkStyle: React.CSSProperties = {
  background: 'none',
  border: 'none',
  padding: 0,
  fontSize: 12,
  fontFamily: 'var(--font-body)',
  color: 'var(--accent-primary)',
  cursor: 'pointer',
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

function getAppDef(id: string): AppDef | undefined {
  return APP_DEFS.find(d => d.id === id);
}

// ---- Toggle Switch ----

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

// ---- Webhook URL row ----

function WebhookUrlRow({ route }: { route: WebhookRoute }) {
  const [copied, setCopied] = useState(false);
  const url = `${window.location.origin}${route.path}`;
  const canCopy = typeof navigator !== 'undefined' && !!navigator.clipboard;

  async function copy() {
    try {
      await navigator.clipboard.writeText(url);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // clipboard unavailable — the URL is still selectable below
    }
  }

  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <code
          style={{
            background: 'var(--bg-surface)',
            border: '1px solid var(--border-default)',
            borderRadius: 'var(--radius-sm)',
            padding: '6px 10px',
            fontSize: 12,
            color: 'var(--text-primary)',
            overflowX: 'auto',
            whiteSpace: 'nowrap',
            flex: 1,
            userSelect: 'all',
          }}
        >
          {url}
        </code>
        {canCopy && (
          <button
            type="button"
            onClick={copy}
            style={{
              background: 'none',
              border: '1px solid var(--border-default)',
              borderRadius: 'var(--radius-sm)',
              padding: '5px 10px',
              cursor: 'pointer',
              fontSize: 12,
              color: copied ? 'var(--accent-primary)' : 'var(--text-muted)',
              fontFamily: 'var(--font-body)',
              flexShrink: 0,
            }}
          >
            {copied ? 'copied' : 'copy'}
          </button>
        )}
      </div>
      {route.authMode === 'HeaderToken' && route.headerName && (
        <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 3 }}>
          requires token in header {route.headerName}
        </div>
      )}
    </div>
  );
}

// ---- ApplicationCard ----

interface ApplicationCardProps {
  app: Application;
  def: AppDef | undefined;
  webhookRoutes: WebhookRoute[];
  onSaved: () => void;
}

const EMPTY_FIELDS: FieldDef[] = [];

function ApplicationCard({ app, def, webhookRoutes, onSaved }: ApplicationCardProps) {
  const fields = def?.fields ?? EMPTY_FIELDS;
  const icon = def?.icon ?? '\u{1F4E6}';
  const description = def?.description ?? '';

  const [enabled, setEnabled] = useState(app.enabled);
  const [settings, setSettings] = useState<Record<string, string>>(() => {
    const defaults: Record<string, string> = {};
    for (const f of fields) {
      defaults[f.key] = app.settings?.[f.key] ?? f.defaultValue ?? '';
    }
    return defaults;
  });
  const [showPassword, setShowPassword] = useState<Record<string, boolean>>({});
  const [expanded, setExpanded] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [showDysonWizard, setShowDysonWizard] = useState(false);
  const [dysonAccount, setDysonAccount] = useState<{ connected: boolean; email?: string; deviceCount?: number } | null>(null);

  // Fetch Dyson account info once (and when wizard completes)
  useEffect(() => {
    if (app.id !== 'dyson') return;
    dysonGetAccount().then(setDysonAccount).catch(() => setDysonAccount(null));
  }, [app.id, showDysonWizard]);

  // Sync when app data reloads
  useEffect(() => {
    setEnabled(app.enabled);
    const next: Record<string, string> = {};
    for (const f of fields) {
      next[f.key] = app.settings?.[f.key] ?? f.defaultValue ?? '';
    }
    setSettings(next);
  }, [app, fields]);

  async function handleSave(overrides?: { enabled?: boolean }) {
    setSaving(true);
    setSaveError(null);
    setSaved(false);
    try {
      await saveApplication(app.id, {
        enabled: overrides?.enabled ?? enabled,
        settings,
      });
      setSaved(true);
      onSaved();
      setTimeout(() => setSaved(false), 2000);
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : 'Failed to save');
    } finally {
      setSaving(false);
    }
  }

  function handleToggle(value: boolean) {
    setEnabled(value);
    handleSave({ enabled: value });
  }

  const sColor = statusColor(app.status);

  return (
    <div style={cardStyle}>
      {/* Header */}
      <div style={cardHeaderStyle}>
        <span style={{ fontSize: 22, lineHeight: 1 }}>{icon}</span>
        <div style={cardTitleStyle}>{app.name}</div>
        <span style={typeBadgeStyle}>{app.type}</span>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginLeft: 'auto' }}>
          <span style={statusDotStyle(sColor)} />
          <span style={statusLabelStyle(sColor)}>{app.status}</span>
        </div>
        <span style={deviceCountStyle}>
          {app.deviceCount} device{app.deviceCount !== 1 ? 's' : ''}
        </span>
        <ToggleSwitch checked={enabled} onChange={handleToggle} />
      </div>

      {/* Description */}
      {description && (
        <div style={{ fontSize: 13, color: 'var(--text-muted)', marginTop: 8 }}>{description}</div>
      )}

      {/* Dyson account status */}
      {app.id === 'dyson' && dysonAccount?.connected && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 6 }}>
          <span style={{ width: 6, height: 6, borderRadius: '50%', background: 'var(--accent-green)', flexShrink: 0, display: 'inline-block' }} />
          <span style={{ fontSize: 12, color: 'var(--text-muted)', fontFamily: 'var(--font-body)' }}>
            Connected as {dysonAccount.email} · {dysonAccount.deviceCount} device{dysonAccount.deviceCount !== 1 ? 's' : ''} ·{' '}
            <button type="button" style={dysonLinkStyle} onClick={() => setShowDysonWizard(true)}>
              Reconnect
            </button>
          </span>
        </div>
      )}
      {app.id === 'dyson' && dysonAccount && !dysonAccount.connected && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 6 }}>
          <span style={{ fontSize: 12, color: 'var(--text-muted)', fontFamily: 'var(--font-body)' }}>
            Account not connected.{' '}
            <button type="button" style={dysonLinkStyle} onClick={() => setShowDysonWizard(true)}>
              Connect now
            </button>
          </span>
        </div>
      )}

      {/* Error banner */}
      {app.errorMessage && (
        <div style={{
          background: 'color-mix(in srgb, var(--accent-red) 12%, transparent)',
          border: '1px solid color-mix(in srgb, var(--accent-red) 35%, transparent)',
          borderRadius: 'var(--radius-sm)',
          padding: '10px 14px',
          marginTop: 12,
          fontSize: 13,
          color: 'var(--accent-red)',
        }}>
          {app.errorMessage}
        </div>
      )}

      {/* Webhook URLs (live registrations) */}
      {webhookRoutes.length > 0 && (
        <div style={{ marginTop: 14 }}>
          <div style={{ ...fieldLabelStyle, marginBottom: 8 }}>Webhooks</div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {webhookRoutes.map(r => (
              <WebhookUrlRow key={r.routeKey} route={r} />
            ))}
          </div>
        </div>
      )}

      {/* Expandable settings */}
      {fields.length > 0 && (
        <>
          <button
            type="button"
            onClick={() => setExpanded(prev => !prev)}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 6,
              marginTop: 16,
              padding: 0,
              background: 'none',
              border: 'none',
              cursor: 'pointer',
              fontSize: 13,
              fontWeight: 600,
              color: 'var(--text-muted)',
              fontFamily: 'var(--font-body)',
            }}
          >
            <span style={{
              display: 'inline-block',
              transition: 'transform 0.15s',
              transform: expanded ? 'rotate(90deg)' : 'rotate(0deg)',
              fontSize: 11,
            }}>
              {'▶'}
            </span>
            Settings
          </button>

          {expanded && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 16, marginTop: 14 }}>
              {fields.map(field => {
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
          )}
        </>
      )}

      {/* Save error */}
      {saveError && (
        <div style={{ fontSize: 13, color: 'var(--accent-red)', marginTop: 12 }}>{saveError}</div>
      )}

      {/* Actions */}
      <div style={{ ...actionsRowStyle, marginTop: 16 }}>
        <button
          className="btn-primary"
          disabled={saving}
          style={{ opacity: saving ? 0.6 : 1 }}
          onClick={() => handleSave()}
        >
          {saving ? 'Saving…' : 'Save'}
        </button>
        {saved && (
          <span style={{ fontSize: 13, color: 'var(--accent-primary)', fontWeight: 500 }}>
            Saved
          </span>
        )}
        {app.id === 'dyson' && (
          <button
            type="button"
            className="btn-secondary"
            onClick={() => setShowDysonWizard(true)}
            style={{ marginLeft: 'auto' }}
          >
            Connect account
          </button>
        )}
      </div>

      {/* Dyson onboarding wizard */}
      {showDysonWizard && (
        <DysonOnboardingWizard
          onClose={() => setShowDysonWizard(false)}
          onSuccess={() => {
            setShowDysonWizard(false);
            onSaved();
          }}
        />
      )}
    </div>
  );
}

// ---- ApplicationsPage ----

export function ApplicationsPage() {
  const [apps, setApps] = useState<Application[]>([]);
  const [webhookRoutes, setWebhookRoutes] = useState<WebhookRoute[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    try {
      const [appsData, routesData] = await Promise.all([
        getApplications(),
        getWebhookRoutes().catch(() => [] as WebhookRoute[]),
      ]);
      setApps(appsData);
      setWebhookRoutes(routesData);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load applications');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  if (loading) {
    return (
      <div className="main-inner">
      <div className="page-content" style={{ color: 'var(--text-muted)', fontFamily: 'var(--font-body)' }}>
        Loading applications...
      </div>
      </div>
    );
  }

  const providers = apps.filter(a => a.type === 'provider');
  const consumers = apps.filter(a => a.type === 'consumer');

  return (
    <div className="main-inner">
    <div className="page-content" style={pageStyle}>
      <div style={headingStyle}>Applications</div>
      <div style={subtitleStyle}>Manage provider and consumer applications.</div>

      {error && (
        <div style={{
          background: 'color-mix(in srgb, var(--accent-red) 12%, transparent)',
          border: '1px solid color-mix(in srgb, var(--accent-red) 35%, transparent)',
          borderRadius: 'var(--radius-sm)',
          padding: '10px 14px',
          marginBottom: 20,
          fontSize: 13,
          color: 'var(--accent-red)',
        }}>
          {error}
        </div>
      )}

      {providers.length > 0 && (
        <>
          <div style={sectionLabelStyle}>Providers</div>
          {providers.map(app => (
            <ApplicationCard key={app.id} app={app} def={getAppDef(app.id)} webhookRoutes={webhookRoutes.filter(r => r.integrationId === app.id)} onSaved={load} />
          ))}
        </>
      )}

      {consumers.length > 0 && (
        <>
          <div style={sectionLabelStyle}>Consumers</div>
          {consumers.map(app => (
            <ApplicationCard key={app.id} app={app} def={getAppDef(app.id)} webhookRoutes={webhookRoutes.filter(r => r.integrationId === app.id)} onSaved={load} />
          ))}
        </>
      )}

      {apps.length === 0 && !error && (
        <div style={{ fontSize: 14, color: 'var(--text-muted)', marginTop: 24 }}>
          No applications found.
        </div>
      )}
    </div>
    </div>
  );
}
