import React from 'react';
import { Outlet } from 'react-router-dom';
import { TabNav } from './TabNav';

export function Layout() {
  const wrapper: React.CSSProperties = {
    minHeight: '100vh',
    display: 'flex',
    flexDirection: 'column',
  };

  const header: React.CSSProperties = {
    backgroundColor: 'var(--bg-card)',
    borderBottom: '1px solid var(--border)',
    padding: '0 24px',
  };

  const headerInner: React.CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    height: 52,
  };

  const logo: React.CSSProperties = {
    fontSize: 18,
    fontWeight: 700,
    color: 'var(--text-primary)',
    letterSpacing: '-0.02em',
    marginRight: 32,
  };

  const main: React.CSSProperties = {
    flex: 1,
    padding: '24px',
    maxWidth: 1200,
    width: '100%',
    margin: '0 auto',
  };

  return (
    <div style={wrapper}>
      <div style={header}>
        <div style={headerInner}>
          <span style={logo}>vidar</span>
          <TabNav />
        </div>
      </div>
      <main style={main}>
        <Outlet />
      </main>
    </div>
  );
}
