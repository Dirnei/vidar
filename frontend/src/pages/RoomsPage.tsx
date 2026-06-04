import React, { useCallback, useEffect, useRef, useState } from 'react';
import type { Room, Device } from '../types';
import { getRooms, createRoom, getDevicesInRoom } from '../api/client';
import { subscribeDeviceState } from '../api/sse';
import { RoomCard } from '../components/RoomCard';

export function RoomsPage() {
  const [rooms, setRooms] = useState<Room[]>([]);
  const [devicesByRoom, setDevicesByRoom] = useState<Record<string, Device[]>>({});
  const [newRoomName, setNewRoomName] = useState('');
  const [loading, setLoading] = useState(true);
  const [addingRoom, setAddingRoom] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const loadData = useCallback(async () => {
    const roomList = await getRooms();
    setRooms(roomList);
    const entries = await Promise.all(
      roomList.map(async (r) => {
        const devices = await getDevicesInRoom(r.id);
        return [r.id, devices] as [string, Device[]];
      })
    );
    setDevicesByRoom(Object.fromEntries(entries));
    setLoading(false);
  }, []);

  useEffect(() => {
    loadData();
    const unsub = subscribeDeviceState(() => loadData());
    return unsub;
  }, [loadData]);

  async function handleAddRoom(e: React.FormEvent) {
    e.preventDefault();
    if (!newRoomName.trim()) return;
    setAddingRoom(true);
    try {
      await createRoom(newRoomName.trim());
      setNewRoomName('');
      await loadData();
    } finally {
      setAddingRoom(false);
    }
  }

  const topBar: React.CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    gap: 10,
    marginBottom: 20,
  };

  const inputStyle: React.CSSProperties = {
    backgroundColor: 'var(--bg-card)',
    border: '1px solid var(--border)',
    borderRadius: 6,
    padding: '8px 12px',
    color: 'var(--text-primary)',
    outline: 'none',
    minWidth: 220,
    fontSize: 14,
  };

  const btnAdd: React.CSSProperties = {
    backgroundColor: 'var(--tab-active)',
    color: '#fff',
    border: 'none',
    borderRadius: 6,
    padding: '8px 16px',
    fontWeight: 500,
    fontSize: 14,
    opacity: addingRoom ? 0.6 : 1,
    cursor: addingRoom ? 'not-allowed' : 'pointer',
  };

  const grid: React.CSSProperties = {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))',
    gap: 16,
  };

  const pageTitle: React.CSSProperties = {
    fontSize: 20,
    fontWeight: 600,
    color: 'var(--text-primary)',
    marginBottom: 16,
  };

  if (loading) {
    return <div style={{ color: 'var(--text-muted)', padding: 24 }}>Loading rooms…</div>;
  }

  return (
    <div>
      <div style={pageTitle}>Rooms</div>

      <form onSubmit={handleAddRoom} style={topBar}>
        <input
          ref={inputRef}
          style={inputStyle}
          type="text"
          placeholder="New room name…"
          value={newRoomName}
          onChange={(e) => setNewRoomName(e.target.value)}
        />
        <button type="submit" style={btnAdd} disabled={addingRoom || !newRoomName.trim()}>
          Add Room
        </button>
      </form>

      {rooms.length === 0 ? (
        <div style={{ color: 'var(--text-dimmed)', fontSize: 14, marginTop: 16 }}>
          No rooms yet. Add one above.
        </div>
      ) : (
        <div style={grid}>
          {rooms.map((room) => (
            <RoomCard
              key={room.id}
              room={room}
              devices={devicesByRoom[room.id] ?? []}
              onDeviceStateChange={loadData}
            />
          ))}
        </div>
      )}
    </div>
  );
}
