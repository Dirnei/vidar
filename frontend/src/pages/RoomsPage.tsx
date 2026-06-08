import React, { useCallback, useEffect, useRef, useState } from 'react';
import type { Room, Device, DeviceGroup } from '../types';
import { getRooms, createRoom, getDevicesInRoom, getRoomGroups } from '../api/client';
import { subscribeDeviceState } from '../api/sse';
import { RoomCard } from '../components/RoomCard';
import { CreateGroupModal } from '../components/CreateGroupModal';

export function RoomsPage() {
  const [rooms, setRooms] = useState<Room[]>([]);
  const [devicesByRoom, setDevicesByRoom] = useState<Record<string, Device[]>>({});
  const [groupsByRoom, setGroupsByRoom] = useState<Record<string, DeviceGroup[]>>({});
  const [newRoomName, setNewRoomName] = useState('');
  const [loading, setLoading] = useState(true);
  const [addingRoom, setAddingRoom] = useState(false);
  const [showCreateGroup, setShowCreateGroup] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const loadData = useCallback(async () => {
    const roomList = await getRooms();
    setRooms(roomList);
    const [deviceEntries, groupEntries] = await Promise.all([
      Promise.all(
        roomList.map(async (r) => {
          const devices = await getDevicesInRoom(r.id);
          return [r.id, devices] as [string, Device[]];
        })
      ),
      Promise.all(
        roomList.map(async (r) => {
          const groups = await getRoomGroups(r.id);
          return [r.id, groups] as [string, DeviceGroup[]];
        })
      ),
    ]);
    setDevicesByRoom(Object.fromEntries(deviceEntries));
    setGroupsByRoom(Object.fromEntries(groupEntries));
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
        style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 28, flexWrap: 'wrap' }}
      >
        <input
          ref={inputRef}
          type="text"
          placeholder="New room name…"
          value={newRoomName}
          onChange={(e) => setNewRoomName(e.target.value)}
          style={{ minWidth: 180, flex: 1, fontFamily: 'var(--font-body)', fontSize: 14 }}
        />
        <button
          type="submit"
          className="btn-primary"
          disabled={addingRoom || !newRoomName.trim()}
        >
          Add Room
        </button>
        <button
          type="button"
          className="btn-secondary"
          onClick={() => setShowCreateGroup(true)}
        >
          Create Group
        </button>
      </form>

      {showCreateGroup && (
        <CreateGroupModal
          rooms={rooms}
          onConfirm={async () => {
            setShowCreateGroup(false);
            await loadData();
          }}
          onCancel={() => setShowCreateGroup(false)}
        />
      )}

      {rooms.length === 0 ? (
        <div style={{ color: 'var(--text-muted)', fontSize: 14, marginTop: 8 }}>
          No rooms yet. Add one above.
        </div>
      ) : (
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fill, minmax(min(360px, 100%), 1fr))',
            gap: 18,
          }}
        >
          {rooms.map((room) => (
            <RoomCard
              key={room.id}
              room={room}
              devices={devicesByRoom[room.id] ?? []}
              groups={groupsByRoom[room.id] ?? []}
              onDeviceStateChange={loadData}
            />
          ))}
        </div>
      )}
    </div>
  );
}
