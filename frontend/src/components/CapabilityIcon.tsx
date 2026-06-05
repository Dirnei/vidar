interface Props {
  capability: string;
  size?: number;
  color?: string;
}

export function CapabilityIcon({ capability, size = 16, color }: Props) {
  const c = color ?? accentFor(capability);
  const s = { width: size, height: size, flexShrink: 0 } as const;

  switch (capability) {
    case 'Cover':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round">
          <rect x="3" y="3" width="18" height="18" rx="2" />
          <line x1="3" y1="9" x2="21" y2="9" />
          <line x1="3" y1="15" x2="21" y2="15" />
          <line x1="12" y1="15" x2="12" y2="21" />
        </svg>
      );
    case 'Switch':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round">
          <circle cx="12" cy="14" r="8" />
          <line x1="12" y1="6" x2="12" y2="2" />
        </svg>
      );
    case 'Dimmer':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round">
          <circle cx="12" cy="12" r="4" />
          <line x1="12" y1="2" x2="12" y2="5" />
          <line x1="12" y1="19" x2="12" y2="22" />
          <line x1="4.22" y1="4.22" x2="6.34" y2="6.34" />
          <line x1="17.66" y1="17.66" x2="19.78" y2="19.78" />
          <line x1="2" y1="12" x2="5" y2="12" />
          <line x1="19" y1="12" x2="22" y2="12" />
          <line x1="4.22" y1="19.78" x2="6.34" y2="17.66" />
          <line x1="17.66" y1="6.34" x2="19.78" y2="4.22" />
        </svg>
      );
    case 'Temperature':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M14 14.76V3.5a2.5 2.5 0 0 0-5 0v11.26a4.5 4.5 0 1 0 5 0z" />
        </svg>
      );
    case 'Motion':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round">
          <path d="M17 4a2 2 0 1 0 0-4 2 2 0 0 0 0 4z" fill={c} stroke="none" />
          <path d="M2 18c3-2 5-6 5-10" />
          <path d="M6 20c3-2 6-7 6-13" />
          <path d="M10 22c3-3 7-9 7-16" />
        </svg>
      );
    case 'Power':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2" />
        </svg>
      );
    case 'Energy':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <rect x="1" y="6" width="18" height="12" rx="2" />
          <line x1="23" y1="10" x2="23" y2="14" />
          <line x1="7" y1="10" x2="7" y2="14" />
          <line x1="11" y1="10" x2="11" y2="14" />
        </svg>
      );
    case 'Humidity':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M12 2.69l5.66 5.66a8 8 0 1 1-11.31 0z" />
        </svg>
      );
    default:
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round">
          <circle cx="12" cy="12" r="10" />
          <circle cx="12" cy="12" r="3" />
        </svg>
      );
  }
}

function accentFor(cap: string): string {
  switch (cap) {
    case 'Switch': case 'Dimmer': return 'var(--accent-primary)';
    case 'Cover': return 'var(--accent-teal)';
    case 'Temperature': return 'var(--accent-red)';
    case 'Motion': return 'var(--accent-green)';
    case 'Power': return 'var(--accent-blue)';
    case 'Energy': return 'var(--accent-green)';
    case 'Humidity': return 'var(--accent-blue)';
    default: return 'var(--text-muted)';
  }
}

export function primaryCapabilityIcon(capabilities: string[]): string {
  const priority = ['Cover', 'Switch', 'Dimmer', 'Motion', 'Temperature', 'Humidity', 'Power', 'Energy'];
  for (const p of priority) {
    if (capabilities.includes(p)) return p;
  }
  return capabilities[0] ?? 'Switch';
}
