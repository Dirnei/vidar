import React from 'react';

interface Props {
  checked: boolean;
  onChange: (checked: boolean) => void;
  disabled?: boolean;
}

export function ToggleSwitch({ checked, onChange, disabled = false }: Props) {
  const track: React.CSSProperties = {
    position: 'relative',
    display: 'inline-flex',
    alignItems: 'center',
    width: 40,
    height: 22,
    borderRadius: 11,
    backgroundColor: checked ? 'var(--accent-green)' : 'var(--border)',
    cursor: disabled ? 'not-allowed' : 'pointer',
    transition: 'background-color 0.2s',
    flexShrink: 0,
    opacity: disabled ? 0.5 : 1,
  };

  const thumb: React.CSSProperties = {
    position: 'absolute',
    left: checked ? 20 : 2,
    width: 18,
    height: 18,
    borderRadius: '50%',
    backgroundColor: '#fff',
    transition: 'left 0.2s',
    boxShadow: '0 1px 3px rgba(0,0,0,0.4)',
  };

  return (
    <div
      style={track}
      onClick={() => !disabled && onChange(!checked)}
      role="switch"
      aria-checked={checked}
      tabIndex={disabled ? -1 : 0}
      onKeyDown={(e) => {
        if (!disabled && (e.key === 'Enter' || e.key === ' ')) {
          e.preventDefault();
          onChange(!checked);
        }
      }}
    >
      <div style={thumb} />
    </div>
  );
}
