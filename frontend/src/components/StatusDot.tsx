import React from 'react';

interface Props {
  active: boolean;
  label?: string;
}

export function StatusDot({ active, label }: Props) {
  const dot: React.CSSProperties = {
    display: 'inline-block',
    width: 8,
    height: 8,
    borderRadius: '50%',
    backgroundColor: active ? 'var(--accent-green)' : 'var(--text-muted)',
    flexShrink: 0,
    boxShadow: active ? '0 0 6px var(--accent-green-dim)' : 'none',
    transition: 'background-color 0.2s, box-shadow 0.2s',
  };

  if (label === undefined) {
    return <span style={dot} />;
  }

  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
      <span style={dot} />
      <span style={{
        color: active ? 'var(--accent-green)' : 'var(--text-muted)',
        fontSize: 13,
        fontFamily: 'var(--font-body)',
      }}>
        {label}
      </span>
    </span>
  );
}
