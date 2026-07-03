import React, { useEffect, useState, useCallback } from 'react';
import {
  getThresholdRules,
  createThresholdRule,
  updateThresholdRule,
  deleteThresholdRule,
  getThresholdEvents,
  getDevices,
} from '../api/client';
import type {
  ThresholdRule,
  ThresholdOperator,
  ThresholdEventLog,
  ThresholdEventPage,
  Device,
} from '../types';

import type { UnitType, ValueKind, CapabilityDescriptor } from '../types';

function valueKindFor(unit: UnitType): ValueKind {
  switch (unit) {
    case 'Watts': case 'Kilowatts': case 'WattHours': case 'KilowattHours':
    case 'Celsius': case 'Fahrenheit': case 'Percent': case 'Lux': case 'Number':
    case 'Hectopascals': case 'KilometersPerHour': case 'Millimeters':
    case 'Degrees': case 'UvIndex': case 'WattsPerSquareMeter':
      return 'Numeric';
    case 'OnOff': case 'OpenClosed': case 'Detected': case 'YesNo':
    case 'Action':
      return 'Boolean';
    case 'Text': case 'Url':
      return 'String';
  }
}

const ALL_OPERATORS: { value: ThresholdOperator; label: string; kinds: ValueKind[] }[] = [
  { value: 'GreaterThan', label: '>', kinds: ['Numeric'] },
  { value: 'LessThan', label: '<', kinds: ['Numeric'] },
  { value: 'GreaterThanOrEqual', label: '>=', kinds: ['Numeric'] },
  { value: 'LessThanOrEqual', label: '<=', kinds: ['Numeric'] },
  { value: 'CrossesAbove', label: 'Crosses above', kinds: ['Numeric'] },
  { value: 'CrossesBelow', label: 'Crosses below', kinds: ['Numeric'] },
  { value: 'BecomesTrue', label: 'Becomes true', kinds: ['Boolean'] },
  { value: 'BecomesFalse', label: 'Becomes false', kinds: ['Boolean'] },
  { value: 'Equals', label: '=', kinds: ['String'] },
  { value: 'NotEquals', label: '≠', kinds: ['String'] },
  { value: 'Changes', label: 'Changes', kinds: ['Numeric', 'Boolean', 'String'] },
];

function operatorSymbol(op: ThresholdOperator): string {
  switch (op) {
    case 'GreaterThan': return '>';
    case 'LessThan': return '<';
    case 'GreaterThanOrEqual': return '>=';
    case 'LessThanOrEqual': return '<=';
    case 'CrossesAbove': return '↗';
    case 'CrossesBelow': return '↘';
    case 'BecomesTrue': return '⊤';
    case 'BecomesFalse': return '⊥';
    case 'Changes': return 'Δ';
    case 'Equals': return '=';
    case 'NotEquals': return '≠';
  }
}

function relativeTime(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  if (diff < 0) return 'just now';
  const seconds = Math.floor(diff / 1000);
  if (seconds < 10) return 'just now';
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

// ---- Styles ----

const pageStyle: React.CSSProperties = {
  maxWidth: 900,
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
  boxShadow: 'var(--shadow-card)',
  overflow: 'hidden',
};

const errorBannerStyle: React.CSSProperties = {
  background: 'color-mix(in srgb, var(--accent-red) 12%, transparent)',
  border: '1px solid color-mix(in srgb, var(--accent-red) 35%, transparent)',
  borderRadius: 'var(--radius-sm)',
  padding: '10px 14px',
  marginBottom: 20,
  fontSize: 13,
  color: 'var(--accent-red)',
};

const inputStyle: React.CSSProperties = {
  background: 'var(--bg-surface)',
  border: '1px solid var(--border-default)',
  borderRadius: 'var(--radius-sm)',
  padding: '8px 12px',
  fontSize: 13,
  color: 'var(--text-primary)',
  fontFamily: 'var(--font-body)',
  outline: 'none',
  width: '100%',
  boxSizing: 'border-box',
};

const selectStyle: React.CSSProperties = {
  ...inputStyle,
  cursor: 'pointer',
};

const btnPrimary: React.CSSProperties = {
  background: 'var(--accent-primary)',
  border: 'none',
  borderRadius: 'var(--radius-sm)',
  padding: '8px 18px',
  fontSize: 13,
  fontWeight: 600,
  color: 'var(--bg-base)',
  fontFamily: 'var(--font-body)',
  cursor: 'pointer',
  letterSpacing: '0.01em',
};

const btnGhost: React.CSSProperties = {
  background: 'none',
  border: '1px solid var(--border-default)',
  borderRadius: 'var(--radius-sm)',
  padding: '6px 14px',
  fontSize: 12,
  color: 'var(--text-secondary)',
  fontFamily: 'var(--font-body)',
  cursor: 'pointer',
};

// ---- RuleForm ----

const LEVEL_OPERATORS: ThresholdOperator[] = [
  'GreaterThan', 'LessThan', 'GreaterThanOrEqual', 'LessThanOrEqual',
];

interface RuleFormData {
  name: string;
  deviceId: string;
  capabilityKey: string;
  operator: ThresholdOperator;
  value: string;
  stringValue: string;
  eventName: string;
  enabled: boolean;
  resetValue: string;
}

const emptyForm: RuleFormData = {
  name: '',
  deviceId: '',
  capabilityKey: '',
  operator: 'GreaterThan',
  value: '',
  stringValue: '',
  eventName: '',
  enabled: true,
  resetValue: '',
};

function RuleForm({
  devices,
  initial,
  onSubmit,
  onCancel,
  submitLabel,
}: {
  devices: Device[];
  initial: RuleFormData;
  onSubmit: (data: RuleFormData) => void;
  onCancel: () => void;
  submitLabel: string;
}) {
  const [form, setForm] = useState<RuleFormData>(initial);

  const selectedDevice = devices.find(d => d.id === form.deviceId);
  const capabilities: CapabilityDescriptor[] = selectedDevice?.capabilities ?? [];
  const selectedCap = capabilities.find(c => c.key === form.capabilityKey);
  const kind: ValueKind = selectedCap ? valueKindFor(selectedCap.unit) : 'Numeric';
  const filteredOperators = ALL_OPERATORS.filter(o => o.kinds.includes(kind));
  const needsNumericValue = kind === 'Numeric' && form.operator !== 'Changes';
  const needsStringValue = kind === 'String' && form.operator !== 'Changes';

  function set<K extends keyof RuleFormData>(key: K, val: RuleFormData[K]) {
    setForm(prev => ({ ...prev, [key]: val }));
  }

  return (
    <div style={{ padding: '20px 24px' }}>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 14 }}>
        <div>
          <label style={{ fontSize: 11, color: 'var(--text-muted)', fontWeight: 600, display: 'block', marginBottom: 4 }}>
            Rule Name
          </label>
          <input
            style={inputStyle}
            value={form.name}
            onChange={e => set('name', e.target.value)}
            placeholder="e.g. High Power Alert"
          />
        </div>
        <div>
          <label style={{ fontSize: 11, color: 'var(--text-muted)', fontWeight: 600, display: 'block', marginBottom: 4 }}>
            Event Name
          </label>
          <input
            style={inputStyle}
            value={form.eventName}
            onChange={e => set('eventName', e.target.value)}
            placeholder="e.g. high_power"
          />
        </div>
        <div>
          <label style={{ fontSize: 11, color: 'var(--text-muted)', fontWeight: 600, display: 'block', marginBottom: 4 }}>
            Device
          </label>
          <select
            style={selectStyle}
            value={form.deviceId}
            onChange={e => {
              set('deviceId', e.target.value);
              set('capabilityKey', '');
            }}
          >
            <option value="">Select device...</option>
            {devices.map(d => (
              <option key={d.id} value={d.id}>{d.name}</option>
            ))}
          </select>
        </div>
        <div>
          <label style={{ fontSize: 11, color: 'var(--text-muted)', fontWeight: 600, display: 'block', marginBottom: 4 }}>
            Capability
          </label>
          <select
            style={selectStyle}
            value={form.capabilityKey}
            onChange={e => {
              set('capabilityKey', e.target.value);
              const cap = capabilities.find(c => c.key === e.target.value);
              if (cap) {
                const k = valueKindFor(cap.unit);
                const valid = ALL_OPERATORS.filter(o => o.kinds.includes(k));
                if (!valid.some(o => o.value === form.operator)) {
                  set('operator', valid[0]?.value ?? 'GreaterThan');
                }
              }
            }}
            disabled={!form.deviceId}
          >
            <option value="">Select capability...</option>
            {capabilities.map(c => (
              <option key={c.key} value={c.key}>{c.label}</option>
            ))}
          </select>
        </div>
        <div>
          <label style={{ fontSize: 11, color: 'var(--text-muted)', fontWeight: 600, display: 'block', marginBottom: 4 }}>
            Operator
          </label>
          <select
            style={selectStyle}
            value={form.operator}
            onChange={e => set('operator', e.target.value as ThresholdOperator)}
          >
            {filteredOperators.map(o => (
              <option key={o.value} value={o.value}>{o.label} ({o.value})</option>
            ))}
          </select>
        </div>
        {needsNumericValue && (
          <div>
            <label style={{ fontSize: 11, color: 'var(--text-muted)', fontWeight: 600, display: 'block', marginBottom: 4 }}>
              Value
            </label>
            <input
              type="number"
              step="any"
              style={inputStyle}
              value={form.value}
              onChange={e => set('value', e.target.value)}
              placeholder="0"
            />
          </div>
        )}
        {needsNumericValue && LEVEL_OPERATORS.includes(form.operator) && (
          <div>
            <label style={{ fontSize: 11, color: 'var(--text-muted)', fontWeight: 600, display: 'block', marginBottom: 4 }}>
              Reset Value <span style={{ fontWeight: 400, color: 'var(--text-muted)' }}>(optional)</span>
            </label>
            <input
              type="number"
              step="any"
              style={inputStyle}
              value={form.resetValue}
              onChange={e => set('resetValue', e.target.value)}
              placeholder="Hysteresis reset"
            />
          </div>
        )}
        {needsStringValue && (
          <div>
            <label style={{ fontSize: 11, color: 'var(--text-muted)', fontWeight: 600, display: 'block', marginBottom: 4 }}>
              Value
            </label>
            <input
              style={inputStyle}
              value={form.stringValue}
              onChange={e => set('stringValue', e.target.value)}
              placeholder="e.g. idle"
            />
          </div>
        )}
      </div>
      <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 8 }}>
        Rules fire once when the condition becomes true and re-arm when it becomes false.
        {LEVEL_OPERATORS.includes(form.operator) && ' Set a reset value for hysteresis (e.g. trigger at >30, reset at <28).'}
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginTop: 18 }}>
        <button type="button" style={btnPrimary} onClick={() => onSubmit(form)}>
          {submitLabel}
        </button>
        <button type="button" style={btnGhost} onClick={onCancel}>
          Cancel
        </button>
      </div>
    </div>
  );
}

// ---- RuleRow ----

function RuleRow({
  rule,
  devices,
  onToggle,
  onEdit,
  onDelete,
}: {
  rule: ThresholdRule;
  devices: Device[];
  onToggle: () => void;
  onEdit: () => void;
  onDelete: () => void;
}) {
  const device = devices.find(d => d.id === rule.deviceId);
  const deviceName = device?.name ?? rule.deviceId.slice(0, 8) + '...';

  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: '1fr 140px 200px 60px 80px',
        gap: 0,
        padding: '12px 16px',
        borderBottom: '1px solid var(--border-subtle)',
        fontSize: 13,
        color: 'var(--text-primary)',
        alignItems: 'center',
        opacity: rule.enabled ? 1 : 0.5,
        transition: 'opacity 0.15s',
      }}
    >
      <div>
        <div style={{ fontWeight: 600, marginBottom: 2 }}>{rule.name}</div>
        <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>
          {deviceName} / {rule.capabilityKey}
        </div>
      </div>
      <div>
        <span style={{
          fontSize: 12,
          fontWeight: 600,
          padding: '2px 8px',
          borderRadius: 'var(--radius-sm)',
          background: 'var(--bg-hover)',
          color: 'var(--text-secondary)',
          fontFamily: 'monospace',
        }}>
          {operatorSymbol(rule.operator)} {rule.value}
        </span>
        {rule.resetValue != null && (
          <span style={{ fontSize: 10, color: 'var(--text-muted)', marginLeft: 4 }}>
            ↩{rule.resetValue}
          </span>
        )}
      </div>
      <div>
        <code style={{
          fontSize: 11,
          padding: '2px 7px',
          borderRadius: 'var(--radius-sm)',
          background: 'var(--accent-primary-dim)',
          color: 'var(--accent-primary)',
        }}>
          {rule.eventName}
        </code>
      </div>
      <div>
        <button
          type="button"
          onClick={onToggle}
          style={{
            width: 36, height: 20,
            borderRadius: 10,
            border: 'none',
            background: rule.enabled ? 'var(--accent-green)' : 'var(--bg-hover)',
            cursor: 'pointer',
            position: 'relative',
            transition: 'background 0.2s',
          }}
        >
          <span style={{
            position: 'absolute',
            top: 2, left: rule.enabled ? 18 : 2,
            width: 16, height: 16,
            borderRadius: '50%',
            background: '#fff',
            transition: 'left 0.2s',
            boxShadow: '0 1px 3px rgba(0,0,0,0.3)',
          }} />
        </button>
      </div>
      <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
        <button
          type="button"
          onClick={onEdit}
          style={{
            ...btnGhost,
            padding: '4px 10px',
            fontSize: 11,
          }}
        >
          Edit
        </button>
        <button
          type="button"
          onClick={onDelete}
          style={{
            ...btnGhost,
            padding: '4px 10px',
            fontSize: 11,
            color: 'var(--accent-red)',
            borderColor: 'color-mix(in srgb, var(--accent-red) 35%, transparent)',
          }}
        >
          Del
        </button>
      </div>
    </div>
  );
}

// ---- EventLogTable ----

const EVENT_PAGE_SIZE = 20;

function EventLogTable() {
  const [page, setPage] = useState<ThresholdEventPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [skip, setSkip] = useState(0);

  const fetchEvents = useCallback(async (currentSkip: number) => {
    setLoading(true);
    setError(null);
    try {
      const data = await getThresholdEvents(currentSkip, EVENT_PAGE_SIZE);
      setPage(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load events');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchEvents(skip);
  }, [fetchEvents, skip]);

  const totalCount = page?.totalCount ?? 0;
  const items: ThresholdEventLog[] = page?.items ?? [];
  const showingFrom = totalCount === 0 ? 0 : skip + 1;
  const showingTo = Math.min(skip + EVENT_PAGE_SIZE, totalCount);
  const hasPrev = skip > 0;
  const hasNext = skip + EVENT_PAGE_SIZE < totalCount;

  return (
    <>
      <div style={sectionLabelStyle}>Event Audit Log</div>

      {error && <div style={errorBannerStyle}>{error}</div>}

      {loading && !page && (
        <div style={{ color: 'var(--text-muted)', fontFamily: 'var(--font-body)', padding: '24px 0', textAlign: 'center' }}>
          Loading...
        </div>
      )}

      {!loading && items.length === 0 && !error && (
        <div style={{ textAlign: 'center', padding: '48px 0' }}>
          <div style={{ fontSize: 14, color: 'var(--text-secondary)', marginBottom: 6 }}>
            No threshold events fired yet
          </div>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
            Events will appear here when threshold rules are triggered
          </div>
        </div>
      )}

      {items.length > 0 && (
        <div style={cardStyle}>
          <div style={{
            display: 'grid',
            gridTemplateColumns: '1fr 130px 130px 100px 120px',
            gap: 0,
            padding: '10px 16px',
            borderBottom: '1px solid var(--border-subtle)',
            fontSize: 11,
            fontWeight: 600,
            color: 'var(--text-muted)',
            textTransform: 'uppercase',
            letterSpacing: '0.06em',
          }}>
            <div>Fired</div>
            <div>Rule</div>
            <div>Event</div>
            <div>Capability</div>
            <div style={{ textAlign: 'right' }}>Value / Threshold</div>
          </div>

          {items.map(event => (
            <div
              key={event.id}
              style={{
                display: 'grid',
                gridTemplateColumns: '1fr 130px 130px 100px 120px',
                gap: 0,
                padding: '10px 16px',
                borderBottom: '1px solid var(--border-subtle)',
                fontSize: 13,
                color: 'var(--text-primary)',
                transition: 'background 0.1s',
              }}
              onMouseEnter={e => { e.currentTarget.style.background = 'var(--bg-hover)'; }}
              onMouseLeave={e => { e.currentTarget.style.background = 'transparent'; }}
            >
              <div title={event.firedAt}>{relativeTime(event.firedAt)}</div>
              <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>{event.ruleName}</div>
              <div>
                <code style={{
                  fontSize: 11,
                  padding: '2px 7px',
                  borderRadius: 'var(--radius-sm)',
                  background: 'var(--accent-primary-dim)',
                  color: 'var(--accent-primary)',
                }}>
                  {event.eventName}
                </code>
              </div>
              <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
                {event.capabilityKey}
              </div>
              <div style={{ textAlign: 'right', fontFamily: 'monospace', fontSize: 12, color: 'var(--text-secondary)' }}>
                <span style={{ color: 'var(--text-primary)', fontWeight: 600 }}>{event.currentValue}</span>
                {' '}{operatorSymbol(event.operator)}{' '}
                {event.thresholdValue}
              </div>
            </div>
          ))}

          <div style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '10px 16px',
            fontSize: 12,
            color: 'var(--text-muted)',
          }}>
            <span>Showing {showingFrom}-{showingTo} of {totalCount}</span>
            <div style={{ display: 'flex', gap: 8 }}>
              <button
                type="button"
                disabled={!hasPrev}
                onClick={() => setSkip(s => Math.max(0, s - EVENT_PAGE_SIZE))}
                style={{
                  ...btnGhost,
                  padding: '4px 12px',
                  opacity: hasPrev ? 1 : 0.5,
                  cursor: hasPrev ? 'pointer' : 'default',
                }}
              >
                Previous
              </button>
              <button
                type="button"
                disabled={!hasNext}
                onClick={() => setSkip(s => s + EVENT_PAGE_SIZE)}
                style={{
                  ...btnGhost,
                  padding: '4px 12px',
                  opacity: hasNext ? 1 : 0.5,
                  cursor: hasNext ? 'pointer' : 'default',
                }}
              >
                Next
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

// ---- ThresholdRulesPage ----

export function ThresholdRulesPage() {
  const [rules, setRules] = useState<ThresholdRule[]>([]);
  const [devices, setDevices] = useState<Device[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [editingRule, setEditingRule] = useState<ThresholdRule | null>(null);

  useEffect(() => {
    Promise.all([getThresholdRules(), getDevices()])
      .then(([r, d]) => { setRules(r); setDevices(d); })
      .catch(e => setError(e instanceof Error ? e.message : 'Failed to load'))
      .finally(() => setLoading(false));
  }, []);

  async function handleCreate(form: RuleFormData) {
    try {
      const created = await createThresholdRule({
        name: form.name,
        deviceId: form.deviceId,
        capabilityKey: form.capabilityKey,
        operator: form.operator,
        value: parseFloat(form.value) || 0,
        stringValue: form.stringValue || null,
        eventName: form.eventName,
        enabled: form.enabled,
        resetValue: form.resetValue ? parseFloat(form.resetValue) : null,
      });
      setRules(prev => [...prev, created]);
      setShowCreate(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to create rule');
    }
  }

  async function handleUpdate(form: RuleFormData) {
    if (!editingRule) return;
    try {
      const resetVal = form.resetValue ? parseFloat(form.resetValue) : null;
      await updateThresholdRule(editingRule.id, {
        name: form.name,
        deviceId: form.deviceId,
        capabilityKey: form.capabilityKey,
        operator: form.operator,
        value: parseFloat(form.value) || 0,
        stringValue: form.stringValue || null,
        eventName: form.eventName,
        enabled: form.enabled,
        resetValue: resetVal,
      });
      setRules(prev =>
        prev.map(r => r.id === editingRule.id
          ? { ...r, name: form.name, deviceId: form.deviceId, capabilityKey: form.capabilityKey, operator: form.operator, value: parseFloat(form.value) || 0, stringValue: form.stringValue || null, eventName: form.eventName, enabled: form.enabled, resetValue: resetVal }
          : r
        )
      );
      setEditingRule(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to update rule');
    }
  }

  async function handleToggle(rule: ThresholdRule) {
    try {
      await updateThresholdRule(rule.id, {
        name: rule.name,
        deviceId: rule.deviceId,
        capabilityKey: rule.capabilityKey,
        operator: rule.operator,
        value: rule.value,
        stringValue: rule.stringValue,
        eventName: rule.eventName,
        enabled: !rule.enabled,
        resetValue: rule.resetValue,
      });
      setRules(prev => prev.map(r => r.id === rule.id ? { ...r, enabled: !r.enabled } : r));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to toggle rule');
    }
  }

  async function handleDelete(id: string) {
    try {
      await deleteThresholdRule(id);
      setRules(prev => prev.filter(r => r.id !== id));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to delete rule');
    }
  }

  if (loading) {
    return (
      <div className="main-inner">
        <div className="page-content" style={{ color: 'var(--text-muted)', fontFamily: 'var(--font-body)' }}>
          Loading...
        </div>
      </div>
    );
  }

  return (
    <div className="main-inner">
      <div className="page-content" style={pageStyle}>
        <div style={headingStyle}>Threshold Rules</div>
        <div style={subtitleStyle}>
          Configure metric-based rules that fire events when values cross thresholds.
        </div>

        {error && <div style={errorBannerStyle}>{error}</div>}

        {/* Rules Section */}
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <div style={sectionLabelStyle}>Rules</div>
          {!showCreate && !editingRule && (
            <button
              type="button"
              style={{ ...btnPrimary, fontSize: 12, padding: '6px 14px' }}
              onClick={() => setShowCreate(true)}
            >
              + New Rule
            </button>
          )}
        </div>

        {/* Create form */}
        {showCreate && (
          <div style={{ ...cardStyle, marginBottom: 16 }}>
            <div style={{
              padding: '12px 24px 0',
              fontSize: 13,
              fontWeight: 600,
              color: 'var(--text-primary)',
            }}>
              Create Rule
            </div>
            <RuleForm
              devices={devices}
              initial={emptyForm}
              onSubmit={handleCreate}
              onCancel={() => setShowCreate(false)}
              submitLabel="Create"
            />
          </div>
        )}

        {/* Edit form */}
        {editingRule && (
          <div style={{ ...cardStyle, marginBottom: 16 }}>
            <div style={{
              padding: '12px 24px 0',
              fontSize: 13,
              fontWeight: 600,
              color: 'var(--text-primary)',
            }}>
              Edit Rule
            </div>
            <RuleForm
              devices={devices}
              initial={{
                name: editingRule.name,
                deviceId: editingRule.deviceId,
                capabilityKey: editingRule.capabilityKey,
                operator: editingRule.operator,
                value: String(editingRule.value),
                stringValue: editingRule.stringValue ?? '',
                eventName: editingRule.eventName,
                enabled: editingRule.enabled,
                resetValue: editingRule.resetValue != null ? String(editingRule.resetValue) : '',
              }}
              onSubmit={handleUpdate}
              onCancel={() => setEditingRule(null)}
              submitLabel="Save"
            />
          </div>
        )}

        {/* Rules list */}
        {rules.length === 0 && !showCreate && (
          <div style={{ textAlign: 'center', padding: '48px 0' }}>
            <div style={{ fontSize: 14, color: 'var(--text-secondary)', marginBottom: 6 }}>
              No threshold rules configured
            </div>
            <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
              Create a rule to start monitoring device metrics
            </div>
          </div>
        )}

        {rules.length > 0 && (
          <div style={cardStyle}>
            <div style={{
              display: 'grid',
              gridTemplateColumns: '1fr 140px 200px 60px 80px',
              gap: 0,
              padding: '10px 16px',
              borderBottom: '1px solid var(--border-subtle)',
              fontSize: 11,
              fontWeight: 600,
              color: 'var(--text-muted)',
              textTransform: 'uppercase',
              letterSpacing: '0.06em',
            }}>
              <div>Rule</div>
              <div>Condition</div>
              <div>Event</div>
              <div>Active</div>
              <div />
            </div>

            {rules.map(rule => (
              <RuleRow
                key={rule.id}
                rule={rule}
                devices={devices}
                onToggle={() => handleToggle(rule)}
                onEdit={() => {
                  setEditingRule(rule);
                  setShowCreate(false);
                }}
                onDelete={() => handleDelete(rule.id)}
              />
            ))}
          </div>
        )}

        {/* Audit Log */}
        <EventLogTable />
      </div>
    </div>
  );
}
