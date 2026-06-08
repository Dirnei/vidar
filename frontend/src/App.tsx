import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { ExpertModeProvider } from './components/ExpertMode';
import { Layout } from './components/Layout';
import { RoomsPage } from './pages/RoomsPage';
import { DevicesPage } from './pages/DevicesPage';
import { DiscoveredPage } from './pages/DiscoveredPage';
import { DeviceDetailPage } from './pages/DeviceDetailPage';
import { GroupDetailPage } from './pages/GroupDetailPage';
import { ApplicationsPage } from './pages/ApplicationsPage';

export default function App() {
  return (
    <ExpertModeProvider>
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Layout />}>
          <Route index element={<RoomsPage />} />
          <Route path="devices" element={<DevicesPage />} />
          <Route path="devices/:id" element={<DeviceDetailPage />} />
          <Route path="groups/:id" element={<GroupDetailPage />} />
          <Route path="discovered" element={<DiscoveredPage />} />
          <Route path="applications" element={<ApplicationsPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
    </ExpertModeProvider>
  );
}
