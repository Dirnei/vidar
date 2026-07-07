import type { CapabilityOption } from '../types';

interface Props {
  options: CapabilityOption[];
  value?: number;
  accent: string;
  onChange: (value: number) => void;
}

// Labeled dropdown for an enumerated capability (a ceiling fan's mode, a thermostat's operating
// mode, ...) — one shared renderer so every enumerated `Options` capability looks and behaves
// the same way everywhere it appears, whether inside the generic capability card or a bespoke
// rich card like ClimateCard.
export function EnumPicker({ options, value, accent, onChange }: Props) {
  const selected = options.find(o => o.value === value);
  return (
    <>
      <div style={{ fontFamily: 'var(--font-heading)', fontSize: 22, fontWeight: 600, color: accent, marginBottom: 2 }}>
        {selected ? selected.label : (value ?? '—')}
      </div>
      <select
        value={value === undefined ? '' : String(value)}
        onChange={e => onChange(Number(e.target.value))}
        style={{
          marginTop: 10, width: '100%', padding: '8px 10px',
          background: 'var(--bg-hover)', color: 'var(--text-primary)',
          border: '1px solid var(--border-default)', borderRadius: 'var(--radius-sm)',
          fontFamily: 'var(--font-body)', fontSize: 14, cursor: 'pointer',
        }}
      >
        {value === undefined && <option value="" disabled>—</option>}
        {options.map(o => (
          <option key={o.value} value={String(o.value)}>{o.label}</option>
        ))}
      </select>
    </>
  );
}
