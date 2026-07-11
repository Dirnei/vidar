import { lazy, Suspense } from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { ExpertModeProvider } from './components/ExpertMode';
import { Layout } from './components/Layout';

// Route-level code splitting: only the shell + the landing page are in the initial
// bundle. Detail pages (which pull in the heavy device cards) and the Applications
// page (which pulls in every onboarding wizard) load on demand.
const RoomsPage = lazy(() => import('./pages/RoomsPage').then((m) => ({ default: m.RoomsPage })));
const DevicesPage = lazy(() => import('./pages/DevicesPage').then((m) => ({ default: m.DevicesPage })));
const DeviceDetailPage = lazy(() => import('./pages/DeviceDetailPage').then((m) => ({ default: m.DeviceDetailPage })));
const GroupDetailPage = lazy(() => import('./pages/GroupDetailPage').then((m) => ({ default: m.GroupDetailPage })));
const DiscoveredPage = lazy(() => import('./pages/DiscoveredPage').then((m) => ({ default: m.DiscoveredPage })));
const ApplicationsPage = lazy(() => import('./pages/ApplicationsPage').then((m) => ({ default: m.ApplicationsPage })));
const WebhooksPage = lazy(() => import('./pages/WebhooksPage').then((m) => ({ default: m.WebhooksPage })));
const ThresholdRulesPage = lazy(() => import('./pages/ThresholdRulesPage').then((m) => ({ default: m.ThresholdRulesPage })));

function PageFallback() {
  return (
    <div className="main-inner">
      <div style={{ color: 'var(--text-muted)', padding: 24, fontFamily: 'var(--font-body)' }}>Loading…</div>
    </div>
  );
}

export default function App() {
  return (
    <ExpertModeProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/" element={<Layout />}>
            <Route index element={<Suspense fallback={<PageFallback />}><RoomsPage /></Suspense>} />
            <Route path="devices" element={<Suspense fallback={<PageFallback />}><DevicesPage /></Suspense>} />
            <Route path="devices/:id" element={<Suspense fallback={<PageFallback />}><DeviceDetailPage /></Suspense>} />
            <Route path="groups/:id" element={<Suspense fallback={<PageFallback />}><GroupDetailPage /></Suspense>} />
            <Route path="discovered" element={<Suspense fallback={<PageFallback />}><DiscoveredPage /></Suspense>} />
            <Route path="applications" element={<Suspense fallback={<PageFallback />}><ApplicationsPage /></Suspense>} />
            <Route path="webhooks" element={<Suspense fallback={<PageFallback />}><WebhooksPage /></Suspense>} />
            <Route path="threshold-rules" element={<Suspense fallback={<PageFallback />}><ThresholdRulesPage /></Suspense>} />
          </Route>
        </Routes>
      </BrowserRouter>
    </ExpertModeProvider>
  );
}
