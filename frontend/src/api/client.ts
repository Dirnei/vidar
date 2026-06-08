import type { Room, Device, DiscoveredDevice, DeviceGroup, CommandPayload, ConfigurePayload, StateHistoryEntry, CommandHistoryEntry } from '../types';

const BASE = '/api';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json', ...init?.headers },
    ...init,
  });
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    throw new Error(`${res.status} ${text}`);
  }
  if (res.status === 204 || res.status === 202) return undefined as T;
  return res.json() as Promise<T>;
}

// --- Rooms ---

export function getRooms(): Promise<Room[]> {
  return request('/rooms');
}

export function createRoom(name: string): Promise<Room> {
  return request('/rooms', { method: 'POST', body: JSON.stringify({ name }) });
}

export function updateRoom(id: string, name: string): Promise<Room> {
  return request(`/rooms/${id}`, { method: 'PUT', body: JSON.stringify({ name }) });
}

export function deleteRoom(id: string): Promise<void> {
  return request(`/rooms/${id}`, { method: 'DELETE' });
}

export function getDevicesInRoom(roomId: string): Promise<Device[]> {
  return request(`/rooms/${roomId}/devices`);
}

// --- Devices ---

export function getDevices(): Promise<Device[]> {
  return request('/devices');
}

export function getDevice(id: string): Promise<Device> {
  return request(`/devices/${id}`);
}

export function sendCommand(id: string, payload: CommandPayload): Promise<void> {
  return request(`/devices/${id}/command`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export function updateDevice(id: string, data: Partial<Device>): Promise<Device> {
  return request(`/devices/${id}`, { method: 'PUT', body: JSON.stringify(data) });
}

export function deleteDevice(id: string): Promise<void> {
  return request(`/devices/${id}`, { method: 'DELETE' });
}

// --- Discovered ---

export function getDiscoveredDevices(): Promise<DiscoveredDevice[]> {
  return request('/devices/discovered');
}

export function configureDiscoveredDevice(id: string, payload: ConfigurePayload): Promise<Device> {
  return request(`/devices/discovered/${id}/configure`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export function updateDeviceSettings(id: string, settings: Record<string, string>): Promise<void> {
  return request(`/devices/${id}/settings`, { method: 'PUT', body: JSON.stringify({ settings }) });
}

// --- Groups ---

export function getGroups(): Promise<DeviceGroup[]> {
  return request('/groups');
}

export function getGroup(id: string): Promise<DeviceGroup> {
  return request(`/groups/${id}`);
}

export function createGroup(data: { name: string; roomId: string; deviceIds: string[] }): Promise<DeviceGroup> {
  return request('/groups', { method: 'POST', body: JSON.stringify(data) });
}

export function updateGroup(id: string, data: { name: string; roomId: string; deviceIds: string[] }): Promise<void> {
  return request(`/groups/${id}`, { method: 'PUT', body: JSON.stringify(data) });
}

export function deleteGroup(id: string): Promise<void> {
  return request(`/groups/${id}`, { method: 'DELETE' });
}

export function sendGroupCommand(id: string, payload: CommandPayload): Promise<void> {
  return request(`/groups/${id}/command`, { method: 'POST', body: JSON.stringify(payload) });
}

export function getRoomGroups(roomId: string): Promise<DeviceGroup[]> {
  return request(`/rooms/${roomId}/groups`);
}

// --- Discover ---

export function discoverShellyDevice(host: string): Promise<{ status: string; host: string; message?: string }> {
  return request('/discover/shelly', {
    method: 'POST',
    body: JSON.stringify({ host }),
  });
}

// --- History ---

export function getDeviceStateHistory(id: string, skip = 0, limit = 20): Promise<StateHistoryEntry[]> {
  return request(`/devices/${id}/history/state?skip=${skip}&limit=${limit}`);
}

export function getDeviceCommandHistory(id: string, skip = 0, limit = 20): Promise<CommandHistoryEntry[]> {
  return request(`/devices/${id}/history/commands?skip=${skip}&limit=${limit}`);
}
