import React, { useCallback, useEffect, useState } from 'react';
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

  const pageTitle: React.CSSProperties = {
    fontSize: 20,
    fontWeight: 600,
    color: 'var(--text-primary)',
    marginBottom: 16,
  };

  const filterBar: React.CSSProperties = {
    display: 'flex',
    flexWrap: 'wrap',
    gap: 8,
    marginBottom: 20,
  };

  function filterBtn(active: boolean): React.CSSProperties {
    return {
      padding: '5px 14px',
      borderRadius: 20,
      fontSize: 13,
      fontWeight: 500,
      cursor: 'pointer',
      backgroundColor: active ? 'var(--tab-active)' : 'var(--bg-row)',
      color: active ? '#fff' : 'var(--text-muted)',
      border: active ? '1px solid var(--tab-active)' : '1px solid var(--border)',
      transition: 'all 0.15s',
    };
  }

  const list: React.CSSProperties = {
    display: 'flex',
    flexDirection: 'column',
    gap: 0,
  };

  if (loading) {
    return <div style={{ color: 'var(--text-muted)', padding: 24 }}>Loading devices…</div>;
  }

  return (
    <div>
      <div style={pageTitle}>Devices</div>

      <div style={filterBar}>
        {filters.map((f) => (
          <button
            key={f}
            style={filterBtn(filter === f)}
            onClick={() => setFilter(f)}
          >
            {f}
          </button>
        ))}
      </div>

      {filtered.length === 0 ? (
        <div style={{ color: 'var(--text-dimmed)', fontSize: 14 }}>
          No devices{filter !== 'All' ? ` with capability "${filter}"` : ''}.
        </div>
      ) : (
        <div style={list}>
          {filtered.map((d) => (
            <DeviceRow
              key={d.id}
              device={d}
              showRoom
              rooms={rooms}
              onStateChange={loadData}
            />
          ))}
        </div>
      )}
    </div>
  );
}
