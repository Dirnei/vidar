import type { Room, Device, DiscoveredDevice, CommandPayload, ConfigurePayload } from '../types';

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
  // 204 No Content / 202 Accepted (async, no body)
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

// --- Discover ---

export function discoverShellyDevice(host: string): Promise<void> {
  return request('/discover/shelly', {
    method: 'POST',
    body: JSON.stringify({ host }),
  });
}
