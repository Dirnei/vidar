import { Outlet } from 'react-router-dom';
import { SideNav } from './TabNav';

export function Layout() {
  return (
    <div className="app-shell">
      <SideNav />
      <div className="main-content">
        <Outlet />
      </div>
    </div>
  );
}
