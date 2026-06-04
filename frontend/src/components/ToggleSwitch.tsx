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
    width: 36,
    height: 20,
    borderRadius: 10,
    backgroundColor: checked ? 'var(--accent-primary)' : '#2A2B30',
    cursor: disabled ? 'not-allowed' : 'pointer',
    transition: 'background-color 0.2s, box-shadow 0.2s',
    flexShrink: 0,
    opacity: disabled ? 0.5 : 1,
    boxShadow: checked ? '0 0 8px var(--accent-primary-glow)' : 'none',
  };

  const thumb: React.CSSProperties = {
    position: 'absolute',
    left: checked ? 18 : 2,
    width: 16,
    height: 16,
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
