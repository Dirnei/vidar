import { useCallback, useEffect, useState } from 'react';
import type { Device, Room } from '../types';
import { getDevices, getRooms } from '../api/client';
import { subscribeDeviceState } from '../api/sse';
import { DeviceRow } from '../components/DeviceRow';

type Filter = 'All' | string;

export function DevicesPage() {
  const [devices, setDevices] = useState<Device[]>([]);
  const [rooms, setRooms] = useState<Room[]>([]);
  const [filter, setFilter] = useState<Filter>('All');
  const [loading, setLoading] = useState(true);

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

  // Collect all unique capabilities
  const allCapabilities = Array.from(
    new Set(devices.flatMap((d) => d.capabilities))
  ).sort();

  const filters: Filter[] = ['All', ...allCapabilities];

  const filtered =
    filter === 'All'
      ? devices
      : devices.filter((d) => d.capabilities.includes(filter));

  if (loading) {
    return <div className="main-inner"><div style={{ color: 'var(--text-muted)', padding: 24, fontFamily: 'var(--font-body)' }}>Loading devices…</div></div>;
  }

  return (
    <div className="main-inner">
    <div className="page-content">
      <div className="page-title">All Devices</div>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginBottom: 24 }}>
        {filters.map((f) => (
          <button
            key={f}
            className={`filter-pill${filter === f ? ' active' : ''}`}
            onClick={() => setFilter(f)}
          >
            {f}
          </button>
        ))}
      </div>

      {filtered.length === 0 ? (
        <div style={{ color: 'var(--text-muted)', fontSize: 14 }}>
          No devices{filter !== 'All' ? ` with capability "${filter}"` : ''}.
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
  );
}
