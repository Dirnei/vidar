import { NavLink } from 'react-router-dom';
import { useExpertMode } from './ExpertMode';

const navItems = [
  { to: '/', label: 'Rooms', icon: '◻', end: true },
  { to: '/devices', label: 'All Devices', icon: '◈', end: false },
  { to: '/discovered', label: 'Setup', icon: '⊕', end: true },
  { to: '/integrations', label: 'Integrations', icon: '⚡', end: true },
];

export function SideNav() {
  const { expert, toggle } = useExpertMode();
  return (
    <aside className="sidebar">
      <div className="sidebar-logo">vidar</div>
      <nav className="sidebar-nav">
        {navItems.map(({ to, label, icon, end }) => (
          <NavLink
            key={to}
            to={to}
            end={end}
            className={({ isActive }) =>
              `sidebar-nav-item${isActive ? ' active' : ''}`
            }
          >
            <span className="sidebar-nav-icon">{icon}</span>
            {label}
          </NavLink>
        ))}
      </nav>
      <div style={{ marginTop: 'auto', padding: '16px 20px', borderTop: '1px solid var(--border-subtle)' }}>
        <button
          onClick={toggle}
          style={{
            display: 'flex', alignItems: 'center', gap: 8, width: '100%',
            padding: '6px 0', fontSize: 12, color: expert ? 'var(--accent-primary)' : 'var(--text-muted)',
            cursor: 'pointer', fontFamily: 'var(--font-body)', transition: 'color 0.15s',
          }}
        >
          <span style={{ fontSize: 14 }}>⚙</span>
          Expert Mode
          <span style={{
            marginLeft: 'auto', fontSize: 10, fontWeight: 600,
            padding: '1px 6px', borderRadius: 3,
            background: expert ? 'var(--accent-primary-dim)' : 'var(--bg-hover)',
            color: expert ? 'var(--accent-primary)' : 'var(--text-muted)',
          }}>
            {expert ? 'ON' : 'OFF'}
          </span>
        </button>
      </div>
    </aside>
  );
}

/** @deprecated use SideNav instead */
export function TabNav() {
  return <SideNav />;
}
