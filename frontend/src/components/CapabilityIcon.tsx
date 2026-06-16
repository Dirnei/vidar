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
    case 'Light':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M9 18h6" />
          <path d="M10 22h4" />
          <path d="M15.09 14c.18-.98.65-1.74 1.41-2.5A4.65 4.65 0 0 0 18 8 6 6 0 0 0 6 8c0 1 .23 2.23 1.5 3.5A4.61 4.61 0 0 1 8.91 14" />
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
    case 'Contact':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <rect x="3" y="3" width="7" height="18" rx="1" />
          <rect x="14" y="3" width="7" height="18" rx="1" />
        </svg>
      );
    case 'Action':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <circle cx="12" cy="12" r="10" />
          <circle cx="12" cy="12" r="3" fill={c} />
        </svg>
      );
    case 'Battery':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <rect x="1" y="6" width="18" height="12" rx="2" />
          <line x1="23" y1="10" x2="23" y2="14" />
        </svg>
      );
    case 'Camera':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z" />
          <circle cx="12" cy="13" r="4" />
        </svg>
      );
    case 'Presence':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <circle cx="12" cy="8" r="4" />
          <path d="M4 20c0-4 3.6-7 8-7s8 3 8 7" />
        </svg>
      );
    case 'Update':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
          <polyline points="7 10 12 15 17 10" />
          <line x1="12" y1="15" x2="12" y2="3" />
        </svg>
      );
    case 'SolarProduction':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <circle cx="12" cy="12" r="5" />
          <line x1="12" y1="1" x2="12" y2="3" />
          <line x1="12" y1="21" x2="12" y2="23" />
          <line x1="4.22" y1="4.22" x2="5.64" y2="5.64" />
          <line x1="18.36" y1="18.36" x2="19.78" y2="19.78" />
          <line x1="1" y1="12" x2="3" y2="12" />
          <line x1="21" y1="12" x2="23" y2="12" />
          <line x1="4.22" y1="19.78" x2="5.64" y2="18.36" />
          <line x1="18.36" y1="5.64" x2="19.78" y2="4.22" />
        </svg>
      );
    case 'GridPower':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M18 4v16" />
          <path d="M6 4v16" />
          <path d="M2 8h20" />
          <path d="M2 16h20" />
        </svg>
      );
    case 'Consumption':
      return (
        <svg style={s} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" />
          <polyline points="9 22 9 12 15 12 15 22" />
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
    case 'Switch': case 'Dimmer': case 'Light': return 'var(--accent-primary)';
    case 'Cover': return 'var(--accent-teal)';
    case 'Temperature': return 'var(--accent-red)';
    case 'Motion': return 'var(--accent-green)';
    case 'Power': return 'var(--accent-blue)';
    case 'Energy': return 'var(--accent-green)';
    case 'Humidity': return 'var(--accent-blue)';
    case 'Contact': return 'var(--accent-teal)';
    case 'Action': return 'var(--accent-primary)';
    case 'Battery': return 'var(--accent-green)';
    case 'Presence': return 'var(--accent-green)';
    case 'Camera': return 'var(--accent-blue)';
    case 'Update': return 'var(--accent-primary)';
    case 'SolarProduction': return 'var(--accent-yellow, #f59e0b)';
    case 'GridPower': return 'var(--accent-blue)';
    case 'Consumption': return 'var(--accent-red)';
    default: return 'var(--text-muted)';
  }
}

export function primaryCapabilityIcon(capabilities: string[]): string {
  const priority = ['Camera', 'Light', 'Cover', 'Switch', 'Dimmer', 'Contact', 'Motion', 'Temperature', 'Humidity', 'Power', 'Energy', 'SolarProduction', 'GridPower', 'Consumption', 'Action', 'Battery', 'Presence'];
  for (const p of priority) {
    if (capabilities.includes(p)) return p;
  }
  return capabilities[0] ?? 'Switch';
}
