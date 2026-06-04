import { NavLink } from 'react-router-dom';

const navItems = [
  { to: '/', label: 'Rooms', icon: '◻', end: true },
  { to: '/devices', label: 'All Devices', icon: '◈', end: false },
  { to: '/discovered', label: 'Setup', icon: '⊕', end: true },
];

export function SideNav() {
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
    </aside>
  );
}

/** @deprecated use SideNav instead */
export function TabNav() {
  return <SideNav />;
}
