import React from 'react';

interface Props {
  value: number; // 0-100
  color?: string;
  label?: string;
}

export function ProgressBar({ value, color = 'var(--accent-blue)', label }: Props) {
  const clamped = Math.max(0, Math.min(100, value));

  const container: React.CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    gap: 8,
  };

  const track: React.CSSProperties = {
    flex: 1,
    height: 4,
    borderRadius: 2,
    backgroundColor: 'var(--bg-hover)',
    overflow: 'hidden',
  };

  const fill: React.CSSProperties = {
    height: '100%',
    width: `${clamped}%`,
    borderRadius: 2,
    backgroundColor: color,
    transition: 'width 0.3s',
  };

  const labelStyle: React.CSSProperties = {
    minWidth: 36,
    textAlign: 'right',
    fontSize: 12,
    color: 'var(--text-muted)',
    fontFamily: 'var(--font-body)',
  };

  return (
    <div style={container}>
      <div style={track}>
        <div style={fill} />
      </div>
      {label !== undefined && <span style={labelStyle}>{label}</span>}
    </div>
  );
}
