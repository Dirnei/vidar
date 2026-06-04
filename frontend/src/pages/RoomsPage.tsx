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

  if (loading) {
    return <div style={{ color: 'var(--text-muted)', padding: 24, fontFamily: 'var(--font-body)' }}>Loading rooms…</div>;
  }

  return (
    <div className="page-content">
      <div className="page-title">Rooms</div>

      <form
        onSubmit={handleAddRoom}
        style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 28 }}
      >
        <input
          ref={inputRef}
          type="text"
          placeholder="New room name…"
          value={newRoomName}
          onChange={(e) => setNewRoomName(e.target.value)}
          style={{ minWidth: 240, fontFamily: 'var(--font-body)', fontSize: 14 }}
        />
        <button
          type="submit"
          className="btn-primary"
          disabled={addingRoom || !newRoomName.trim()}
        >
          Add Room
        </button>
      </form>

      {rooms.length === 0 ? (
        <div style={{ color: 'var(--text-muted)', fontSize: 14, marginTop: 8 }}>
          No rooms yet. Add one above.
        </div>
      ) : (
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fill, minmax(360px, 1fr))',
            gap: 18,
          }}
        >
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
