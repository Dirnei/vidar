import { useCallback, useEffect, useMemo, useState } from 'react';
import type { Device, Room, ActiveFilters, FilterSection } from '../types';
import { getDevices, getRooms } from '../api/client';
import { subscribeDeviceState } from '../api/sse';
import { DeviceRow } from '../components/DeviceRow';
import { FilterPanel, MobileFilterDrawer } from '../components/FilterPanel';

export function DevicesPage() {
  const [devices, setDevices] = useState<Device[]>([]);
  const [rooms, setRooms] = useState<Room[]>([]);
  const [loading, setLoading] = useState(true);
  const [filters, setFilters] = useState<ActiveFilters>({
    room: new Set(), capability: new Set(), protocol: new Set(), status: new Set(),
  });
  const [search, setSearch] = useState('');
  const [drawerOpen, setDrawerOpen] = useState(false);

  const loadData = useCallback(async () => {
    const [deviceList, roomList] = await Promise.all([getDevices(), getRooms()]);
    setDevices(deviceList);
    setRooms(roomList);
    setLoading(false);
  }, []);

  useEffect(() => {
    loadData();
    const unsub = subscribeDeviceState(() => loadData());
    return unsub;
  }, [loadData]);

  // Build a room lookup map
  const roomMap = useMemo(() => {
    const map = new Map<string, string>();
    for (const r of rooms) {
      map.set(r.id, r.name);
    }
    return map;
  }, [rooms]);

  // Enrich devices with resolved room name
  const devicesWithMeta = useMemo(
    () => devices.map(d => ({
      ...d,
      roomName: d.roomId ? (roomMap.get(d.roomId) ?? 'Unassigned') : 'Unassigned',
    })),
    [devices, roomMap],
  );

  // Search filter
  const searchFiltered = useMemo(() => {
    if (!search) return devicesWithMeta;
    const q = search.toLowerCase();
    return devicesWithMeta.filter(d =>
      d.name.toLowerCase().includes(q) ||
      d.communicationType.toLowerCase().includes(q) ||
      d.capabilities.join(' ').toLowerCase().includes(q)
    );
  }, [devicesWithMeta, search]);

  // Apply all filters
  const filtered = useMemo(() => {
    return searchFiltered.filter(d => {
      if (filters.room.size > 0 && !filters.room.has(d.roomName)) return false;
      if (filters.capability.size > 0 && !d.capabilities.some(c => filters.capability.has(c))) return false;
      if (filters.protocol.size > 0 && !filters.protocol.has(d.communicationType)) return false;
      if (filters.status.size > 0) {
        const status = d.online === false ? 'Offline' : 'Online';
        if (!filters.status.has(status)) return false;
      }
      return true;
    });
  }, [searchFiltered, filters]);

  // Faceted filter sections: each section's counts reflect items passing ALL OTHER filters
  const filterSections = useMemo((): FilterSection[] => {
    // For room counts: apply capability + protocol + status filters
    const forRoom = searchFiltered.filter(d => {
      if (filters.capability.size > 0 && !d.capabilities.some(c => filters.capability.has(c))) return false;
      if (filters.protocol.size > 0 && !filters.protocol.has(d.communicationType)) return false;
      if (filters.status.size > 0) {
        const s = d.online === false ? 'Offline' : 'Online';
        if (!filters.status.has(s)) return false;
      }
      return true;
    });

    // For capability counts: apply room + protocol + status filters
    const forCapability = searchFiltered.filter(d => {
      if (filters.room.size > 0 && !filters.room.has(d.roomName)) return false;
      if (filters.protocol.size > 0 && !filters.protocol.has(d.communicationType)) return false;
      if (filters.status.size > 0) {
        const s = d.online === false ? 'Offline' : 'Online';
        if (!filters.status.has(s)) return false;
      }
      return true;
    });

    // For protocol counts: apply room + capability + status filters
    const forProtocol = searchFiltered.filter(d => {
      if (filters.room.size > 0 && !filters.room.has(d.roomName)) return false;
      if (filters.capability.size > 0 && !d.capabilities.some(c => filters.capability.has(c))) return false;
      if (filters.status.size > 0) {
        const s = d.online === false ? 'Offline' : 'Online';
        if (!filters.status.has(s)) return false;
      }
      return true;
    });

    // For status counts: apply room + capability + protocol filters
    const forStatus = searchFiltered.filter(d => {
      if (filters.room.size > 0 && !filters.room.has(d.roomName)) return false;
      if (filters.capability.size > 0 && !d.capabilities.some(c => filters.capability.has(c))) return false;
      if (filters.protocol.size > 0 && !filters.protocol.has(d.communicationType)) return false;
      return true;
    });

    // Room counts
    const roomCounts = new Map<string, number>();
    for (const d of forRoom) {
      roomCounts.set(d.roomName, (roomCounts.get(d.roomName) ?? 0) + 1);
    }

    // Capability counts
    const capabilityCounts = new Map<string, number>();
    for (const d of forCapability) {
      for (const cap of d.capabilities) {
        capabilityCounts.set(cap, (capabilityCounts.get(cap) ?? 0) + 1);
      }
    }

    // Protocol counts
    const protocolCounts = new Map<string, number>();
    for (const d of forProtocol) {
      protocolCounts.set(d.communicationType, (protocolCounts.get(d.communicationType) ?? 0) + 1);
    }

    // Status counts
    const statusCounts = new Map<string, number>();
    for (const d of forStatus) {
      const status = d.online === false ? 'Offline' : 'Online';
      statusCounts.set(status, (statusCounts.get(status) ?? 0) + 1);
    }

    return [
      {
        key: 'room',
        label: 'Room',
        options: [...roomCounts.entries()]
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
      {
        key: 'protocol',
        label: 'Protocol',
        options: [...protocolCounts.entries()]
          .sort((a, b) => b[1] - a[1])
          .map(([value, count]) => ({ value, label: value, count })),
      },
      {
        key: 'status',
        label: 'Status',
        options: [...statusCounts.entries()]
          .sort((a, b) => b[1] - a[1])
          .map(([value, count]) => ({ value, label: value, count })),
      },
    ];
  }, [searchFiltered, filters]);

  const hasActiveFilters = Object.values(filters).some(s => s.size > 0);

  if (loading) {
    return (
      <div className="content-with-filters">
        <div className="main-inner">
          <div style={{ color: 'var(--text-muted)', padding: 24, fontFamily: 'var(--font-body)' }}>
            Loading devices…
          </div>
        </div>
      </div>
    );
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
            <div className="page-title" style={{ marginBottom: 0 }}>All Devices</div>
            <button
              className="btn-secondary filter-toggle-btn"
              onClick={() => setDrawerOpen(true)}
              style={{ fontSize: 12, padding: '5px 12px' }}
            >
              Filters{hasActiveFilters ? ' *' : ''}
            </button>
          </div>

          <div style={{ fontSize: 13, color: 'var(--text-muted)', marginBottom: 24 }}>
            {filtered.length} of {devices.length} devices
          </div>

          {filtered.length === 0 ? (
            <div style={{ color: 'var(--text-muted)', fontSize: 14 }}>
              {devices.length === 0 ? 'No devices found.' : 'No devices match your filter.'}
            </div>
          ) : (
            <div
              style={{
                background: 'var(--bg-elevated)',
                border: '1px solid var(--border-subtle)',
                borderRadius: 'var(--radius-lg)',
                padding: '4px 20px',
                boxShadow: 'var(--shadow-card)',
              }}
            >
              {filtered.map((d) => (
                <DeviceRow
                  key={d.id}
                  device={d}
                  showRoom
                  rooms={rooms}
                  onStateChange={loadData}
                  groupLabel={d.groupName ? `Group: ${d.groupName}` : undefined}
                />
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
