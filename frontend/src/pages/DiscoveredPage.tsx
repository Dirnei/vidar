import React, { useCallback, useEffect, useMemo, useState } from 'react';
import type { DiscoveredDevice, Room, ActiveFilters, FilterSection } from '../types';
import { getDiscoveredDevices, getRooms, configureDiscoveredDevice, discoverShellyDevice } from '../api/client';
import { ConfigureDeviceModal } from '../components/ConfigureDeviceModal';
import { CapabilityIcon, primaryCapabilityIcon } from '../components/CapabilityIcon';
import { FilterPanel, MobileFilterDrawer } from '../components/FilterPanel';
import { deriveDeviceType } from '../utils/deviceType';

export function DiscoveredPage() {
  const [discovered, setDiscovered] = useState<DiscoveredDevice[]>([]);
  const [rooms, setRooms] = useState<Room[]>([]);
  const [loading, setLoading] = useState(true);
  const [configuring, setConfiguring] = useState<DiscoveredDevice | null>(null);
  const [shellyHost, setShellyHost] = useState('');
  const [probing, setProbing] = useState(false);
  const [feedback, setFeedback] = useState<{ type: 'success' | 'error'; message: string } | null>(null);
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<ActiveFilters>({ protocol: new Set(), deviceType: new Set(), capability: new Set() });
  const [drawerOpen, setDrawerOpen] = useState(false);

  const loadData = useCallback(async () => {
    const [devs, roomList] = await Promise.all([getDiscoveredDevices(), getRooms()]);
    setDiscovered(devs);
    setRooms(roomList);
    setLoading(false);
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  async function handleConfigure(name: string, roomId: string) {
    if (!configuring) return;
    await configureDiscoveredDevice(configuring.id, { name, roomId });
    setConfiguring(null);
    await loadData();
  }

  async function handleDiscoverShelly() {
    const host = shellyHost.trim();
    if (!host) return;
    setProbing(true);
    setFeedback(null);
    try {
      const result = await discoverShellyDevice(host);
      if (result.status === 'discovered') {
        setFeedback({ type: 'success', message: `Device found at ${host}` });
        setShellyHost('');
      } else {
        setFeedback({ type: 'error', message: result.message ?? `No device found at ${host}` });
      }
      await loadData();
    } catch (err) {
      setFeedback({ type: 'error', message: `Failed to reach ${host}: ${err instanceof Error ? err.message : 'unknown error'}` });
    } finally {
      setProbing(false);
    }
  }

  // Derive device type for each discovered device
  const devicesWithType = useMemo(
    () => discovered.map(d => ({ ...d, deviceType: deriveDeviceType(d.capabilities.map(c => c.key), d.metadata) })),
    [discovered],
  );

  // Search filter
  const searchFiltered = useMemo(() => {
    if (!search) return devicesWithType;
    const q = search.toLowerCase();
    return devicesWithType.filter(d => {
      const name = (d.metadata?.name ?? d.metadata?.friendly_name ?? d.nativeId).toLowerCase();
      const nid = d.nativeId.toLowerCase();
      const caps = d.capabilities.map(c => c.label).join(' ').toLowerCase();
      return name.includes(q) || nid.includes(q) || caps.includes(q) ||
        Object.values(d.metadata || {}).some(v => v.toLowerCase().includes(q));
    });
  }, [devicesWithType, search]);

  // Apply all filters
  const filtered = useMemo(() => {
    return searchFiltered.filter(d => {
      if (filters.protocol.size > 0 && !filters.protocol.has(d.communicationType)) return false;
      if (filters.deviceType.size > 0 && !filters.deviceType.has(d.deviceType)) return false;
      if (filters.capability.size > 0 && !d.capabilities.some(c => filters.capability.has(c.key))) return false;
      return true;
    });
  }, [searchFiltered, filters]);

  // Faceted filter sections: each section's counts reflect items passing ALL OTHER filters
  const filterSections = useMemo((): FilterSection[] => {
    // Items passing protocol + capability filters (for device type counts)
    const forDeviceType = searchFiltered.filter(d => {
      if (filters.protocol.size > 0 && !filters.protocol.has(d.communicationType)) return false;
      if (filters.capability.size > 0 && !d.capabilities.some(c => filters.capability.has(c.key))) return false;
      return true;
    });

    // Items passing deviceType + capability filters (for protocol counts)
    const forProtocol = searchFiltered.filter(d => {
      if (filters.deviceType.size > 0 && !filters.deviceType.has(d.deviceType)) return false;
      if (filters.capability.size > 0 && !d.capabilities.some(c => filters.capability.has(c.key))) return false;
      return true;
    });

    // Items passing protocol + deviceType filters (for capability counts)
    const forCapability = searchFiltered.filter(d => {
      if (filters.protocol.size > 0 && !filters.protocol.has(d.communicationType)) return false;
      if (filters.deviceType.size > 0 && !filters.deviceType.has(d.deviceType)) return false;
      return true;
    });

    // Protocol counts
    const protocolCounts = new Map<string, number>();
    for (const d of forProtocol) {
      protocolCounts.set(d.communicationType, (protocolCounts.get(d.communicationType) ?? 0) + 1);
    }

    // Device type counts
    const deviceTypeCounts = new Map<string, number>();
    for (const d of forDeviceType) {
      deviceTypeCounts.set(d.deviceType, (deviceTypeCounts.get(d.deviceType) ?? 0) + 1);
    }

    // Capability counts
    const capabilityCounts = new Map<string, number>();
    for (const d of forCapability) {
      for (const cap of d.capabilities) {
        capabilityCounts.set(cap.key, (capabilityCounts.get(cap.key) ?? 0) + 1);
      }
    }

    return [
      {
        key: 'protocol',
        label: 'Protocol',
        options: [...protocolCounts.entries()]
          .sort((a, b) => b[1] - a[1])
          .map(([value, count]) => ({ value, label: value, count })),
      },
      {
        key: 'deviceType',
        label: 'Device Type',
        options: [...deviceTypeCounts.entries()]
          .sort((a, b) => b[1] - a[1])
          .map(([value, count]) => ({ value, label: value, count })),
      },
      {
        key: 'capability',
        label: 'Capabilities',
        options: [...capabilityCounts.entries()]
          .sort((a, b) => b[1] - a[1])
          .map(([value, count]) => ({ value, label: value, count })),
      },
    ];
  }, [searchFiltered, filters]);

  const hasActiveFilters = Object.values(filters).some(s => s.size > 0);

  const inputStyle: React.CSSProperties = {
    flex: 1,
    minWidth: 0,
    padding: '9px 13px',
    backgroundColor: 'var(--bg-hover)',
    border: '1px solid var(--border-default)',
    borderRadius: 'var(--radius-sm)',
    color: 'var(--text-primary)',
    fontSize: 14,
    outline: 'none',
    fontFamily: 'var(--font-body)',
    transition: 'border-color 0.2s, box-shadow 0.2s',
  };

  function handleInputFocus(e: React.FocusEvent<HTMLInputElement>) {
    e.target.style.borderColor = 'var(--accent-primary)';
    e.target.style.boxShadow = '0 0 0 3px var(--accent-primary-dim)';
  }

  function handleInputBlur(e: React.FocusEvent<HTMLInputElement>) {
    e.target.style.borderColor = 'var(--border-default)';
    e.target.style.boxShadow = 'none';
  }

  if (loading) {
    return <div className="main-inner"><div style={{ color: 'var(--text-muted)', padding: 24, fontFamily: 'var(--font-body)' }}>Loading discovered devices…</div></div>;
  }

  const filterPanelEl = (
    <FilterPanel
      sections={filterSections}
      activeFilters={filters}
      onFilterChange={setFilters}
      search={search}
      onSearchChange={setSearch}
      searchPlaceholder="Search devices..."
    />
  );

  return (
    <div className="content-with-filters">
      {filterPanelEl}
      <MobileFilterDrawer open={drawerOpen} onClose={() => setDrawerOpen(false)}>
        {filterPanelEl}
      </MobileFilterDrawer>
      <div className="main-inner">
        <div className="page-content">
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap', marginBottom: 8 }}>
            <div className="page-title" style={{ marginBottom: 0 }}>Setup</div>
            <button
              className="btn-secondary filter-toggle-btn"
              onClick={() => setDrawerOpen(true)}
              style={{ fontSize: 12, padding: '5px 12px' }}
            >
              Filters{hasActiveFilters ? ' *' : ''}
            </button>
          </div>

          <div style={{ fontSize: 13, color: 'var(--text-muted)', marginBottom: 24 }}>
            {filtered.length} of {discovered.length} device{discovered.length !== 1 ? 's' : ''}
          </div>

          {/* Add Shelly device card */}
          <div
            style={{
              background: 'var(--bg-elevated)',
              border: '1px solid var(--border-subtle)',
              borderRadius: 'var(--radius-lg)',
              padding: '20px 22px',
              marginBottom: 28,
              boxShadow: 'var(--shadow-card)',
            }}
          >
            <div
              style={{
                fontFamily: 'var(--font-heading)',
                fontSize: 14,
                fontWeight: 600,
                color: 'var(--text-secondary)',
                marginBottom: 14,
                letterSpacing: '-0.01em',
              }}
            >
              Add Shelly Device by IP
            </div>
            <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
              <input
                style={inputStyle}
                type="text"
                placeholder="Shelly IP address (e.g. 192.168.1.42)"
                value={shellyHost}
                disabled={probing}
                onChange={(e) => setShellyHost(e.target.value)}
                onFocus={handleInputFocus}
                onBlur={handleInputBlur}
                onKeyDown={(e) => { if (e.key === 'Enter') handleDiscoverShelly(); }}
              />
              <button
                className="btn-primary"
                style={{ flexShrink: 0, opacity: probing ? 0.5 : 1 }}
                disabled={probing}
                onClick={handleDiscoverShelly}
              >
                {probing ? 'Probing…' : 'Discover'}
              </button>
            </div>
            {feedback && (
              <div className={`feedback-banner ${feedback.type}`}>
                <span>{feedback.type === 'success' ? '✓' : '✕'}</span>
                {feedback.message}
              </div>
            )}
          </div>

          {/* Discovered devices list */}
          {filtered.length === 0 ? (
            <div style={{ color: 'var(--text-muted)', fontSize: 14 }}>
              {discovered.length === 0 ? 'No unconfigured devices found.' : 'No devices match your filter.'}
            </div>
          ) : (
            <div>
              {filtered.map((d) => (
                <div key={d.id} className="discovery-card">
                  {/* Large icon */}
                  <div style={{
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    width: 52, height: 52, borderRadius: 'var(--radius-sm)', flexShrink: 0,
                    background: 'var(--bg-hover)', border: '1px solid var(--border-subtle)',
                  }}>
                    <CapabilityIcon capability={primaryCapabilityIcon(d.capabilities)} size={28} />
                  </div>
                  {/* Info */}
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{
                      fontFamily: 'var(--font-heading)', fontSize: 15, fontWeight: 600,
                      color: 'var(--text-primary)', marginBottom: 2,
                    }}>
                      {d.metadata?.name ?? d.metadata?.friendly_name ?? d.nativeId}
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 6 }}>
                      {(d.metadata?.name || d.metadata?.friendly_name) && (
                        <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>{d.nativeId}</span>
                      )}
                      <span style={{
                        fontSize: 10, fontWeight: 600, textTransform: 'uppercase' as const,
                        letterSpacing: '0.05em', padding: '1px 7px', borderRadius: 3,
                        background: d.communicationType === 'shelly'
                          ? 'color-mix(in srgb, var(--accent-primary) 15%, transparent)'
                          : 'color-mix(in srgb, var(--accent-teal) 15%, transparent)',
                        color: d.communicationType === 'shelly' ? 'var(--accent-primary)' : 'var(--accent-teal)',
                      }}>
                        {d.communicationType}
                      </span>
                      <span style={{
                        fontSize: 10, fontWeight: 500, padding: '1px 7px', borderRadius: 3,
                        background: 'var(--bg-hover)', color: 'var(--text-muted)',
                        border: '1px solid var(--border-subtle)',
                      }}>
                        {d.deviceType}
                      </span>
                      {d.metadata?.state && (() => {
                        const s = d.metadata.state.toUpperCase();
                        const online = s === 'ONLINE' || s === 'CONNECTED';
                        const offline = s === 'OFFLINE' || s === 'DISCONNECTED';
                        if (!online && !offline) return null;
                        return (
                          <span style={{
                            fontSize: 10, fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: 4,
                            color: online ? 'var(--accent-green)' : 'var(--accent-red)',
                          }}>
                            <span style={{
                              width: 6, height: 6, borderRadius: '50%',
                              background: online ? 'var(--accent-green)' : 'var(--accent-red)',
                            }} />
                            {online ? 'Online' : 'Offline'}
                          </span>
                        );
                      })()}
                    </div>
                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                      {d.capabilities.map((cap) => (
                        <span
                          key={cap.key}
                          style={{
                            display: 'inline-flex', alignItems: 'center', gap: 4,
                            padding: '2px 9px', borderRadius: 4, fontSize: 11, fontWeight: 500,
                            backgroundColor: 'var(--bg-hover)', color: 'var(--text-secondary)',
                            border: '1px solid var(--border-subtle)',
                          }}
                        >
                          <CapabilityIcon capability={cap.key} size={11} />
                          {cap.label}
                        </span>
                      ))}
                    </div>
                    {d.metadata?.vendor && (
                      <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 6 }}>
                        {[d.metadata.vendor, d.metadata.model].filter(Boolean).join(' · ')}
                      </div>
                    )}
                  </div>
                  <button
                    className="btn-primary"
                    style={{ flexShrink: 0, alignSelf: 'center', fontSize: 13, padding: '7px 16px' }}
                    onClick={() => setConfiguring(d)}
                  >
                    Configure
                  </button>
                </div>
              ))}
            </div>
          )}

          {configuring && rooms.length > 0 && (
            <ConfigureDeviceModal
              rooms={rooms}
              defaultName={configuring.metadata?.name ?? configuring.metadata?.friendly_name}
              defaultRoomId={configuring.capabilities.some(c => c.key === 'presence') ? rooms.find(r => r.isHome)?.id : undefined}
              onConfirm={handleConfigure}
              onCancel={() => setConfiguring(null)}
            />
          )}

          {configuring && rooms.length === 0 && (
            <div className="modal-overlay" onClick={() => setConfiguring(null)}>
              <div className="modal-dialog" style={{ maxWidth: 320, textAlign: 'center' }}>
                <div className="modal-title">No Rooms Available</div>
                <div style={{ fontSize: 13, color: 'var(--text-muted)', marginBottom: 4 }}>
                  Please create a room first before configuring devices.
                </div>
                <div style={{ display: 'flex', justifyContent: 'center', marginTop: 8 }}>
                  <button className="btn-secondary" onClick={() => setConfiguring(null)}>
                    Close
                  </button>
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
