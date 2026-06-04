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
    backgroundColor: active ? 'var(--accent-blue)' : 'var(--text-dimmed)',
    flexShrink: 0,
  };

  if (label === undefined) {
    return <span style={dot} />;
  }

  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
      <span style={dot} />
      <span style={{ color: active ? 'var(--text-primary)' : 'var(--text-dimmed)', fontSize: 13 }}>
        {label}
      </span>
    </span>
  );
}
