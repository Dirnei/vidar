import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { Layout } from './components/Layout';
import { RoomsPage } from './pages/RoomsPage';
import { DevicesPage } from './pages/DevicesPage';
import { DiscoveredPage } from './pages/DiscoveredPage';
import { DeviceDetailPage } from './pages/DeviceDetailPage';

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Layout />}>
          <Route index element={<RoomsPage />} />
          <Route path="devices" element={<DevicesPage />} />
          <Route path="devices/:id" element={<DeviceDetailPage />} />
          <Route path="discovered" element={<DiscoveredPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
