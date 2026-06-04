import React from 'react';
import { NavLink } from 'react-router-dom';

const tabs = [
  { to: '/', label: 'Rooms', end: true },
  { to: '/devices', label: 'Devices', end: false },
  { to: '/discovered', label: 'Discovered', end: true },
];

export function TabNav() {
  const nav: React.CSSProperties = {
    display: 'flex',
    gap: 0,
    borderBottom: '1px solid var(--border)',
  };

  return (
    <nav style={nav}>
      {tabs.map(({ to, label, end }) => (
        <NavLink
          key={to}
          to={to}
          end={end}
          style={({ isActive }) => ({
            padding: '10px 20px',
            fontSize: 14,
            fontWeight: 500,
            color: isActive ? 'var(--text-primary)' : 'var(--text-muted)',
            borderBottom: isActive ? '2px solid var(--tab-active)' : '2px solid transparent',
            marginBottom: -1,
            transition: 'color 0.15s',
            display: 'block',
          })}
        >
          {label}
        </NavLink>
      ))}
    </nav>
  );
}
