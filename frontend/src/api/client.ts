import type { Room, Device, DiscoveredDevice, DeviceGroup, CommandPayload, ConfigurePayload, StateHistoryEntry, CommandHistoryEntry, Application, WebhookRoute, WebhookEventPage, ThresholdRule, ThresholdEventPage, DysonDevice, RoborockDevice, BambuPrinter } from '../types';

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

// --- Applications ---

export function getApplications(): Promise<Application[]> {
  return request('/applications');
}

export function getApplication(id: string): Promise<Application> {
  return request(`/applications/${id}`);
}

export function saveApplication(id: string, data: { enabled: boolean; settings: Record<string, string> }): Promise<void> {
  return request(`/applications/${id}`, { method: 'PUT', body: JSON.stringify(data) });
}

export function getWebhookRoutes(): Promise<WebhookRoute[]> {
  return request('/webhooks/routes');
}

// --- Snapshots ---

export function snapshotUrl(deviceId: string): string {
  return `${BASE}/devices/${deviceId}/snapshot`;
}

export async function getWebhookEvents(
  routeKey?: string,
  skip = 0,
  take = 20,
): Promise<WebhookEventPage> {
  const params = new URLSearchParams();
  if (routeKey) params.set('routeKey', routeKey);
  params.set('skip', String(skip));
  params.set('take', String(take));
  return request<WebhookEventPage>(`/webhooks/events?${params}`);
}

export async function getWebhookPayload(payloadId: string): Promise<string> {
  const res = await fetch(`${BASE}/webhooks/payloads/${payloadId}`);
  if (!res.ok) throw new Error(`${res.status}`);
  return res.text();
}

// --- Threshold Rules ---

export function getThresholdRules(): Promise<ThresholdRule[]> {
  return request('/threshold-rules');
}

export function createThresholdRule(data: Omit<ThresholdRule, 'id'>): Promise<ThresholdRule> {
  return request('/threshold-rules', { method: 'POST', body: JSON.stringify(data) });
}

export function updateThresholdRule(id: string, data: Omit<ThresholdRule, 'id'>): Promise<void> {
  return request(`/threshold-rules/${id}`, { method: 'PUT', body: JSON.stringify(data) });
}

export function deleteThresholdRule(id: string): Promise<void> {
  return request(`/threshold-rules/${id}`, { method: 'DELETE' });
}

// --- Threshold Events ---

export function getThresholdEvents(skip = 0, take = 20): Promise<ThresholdEventPage> {
  return request(`/threshold-events?skip=${skip}&take=${take}`);
}

// --- Dyson ---

export function dysonBeginAuth(region: string, email: string): Promise<{ challengeId: string }> {
  return request('/dyson/auth/begin', { method: 'POST', body: JSON.stringify({ region, email }) });
}

export function dysonVerifyAuth(body: { region: string; email: string; password: string; challengeId: string; otp: string }): Promise<DysonDevice[]> {
  return request('/dyson/auth/verify', { method: 'POST', body: JSON.stringify(body) });
}

export function dysonGetAccount(): Promise<{ connected: boolean; email?: string; deviceCount?: number }> {
  return request('/dyson/account');
}

// --- Roborock ---

export function roborockLogin(email: string, password: string): Promise<RoborockDevice[]> {
  return request('/roborock/login', { method: 'POST', body: JSON.stringify({ email, password }) });
}

export function roborockRequestCode(email: string): Promise<{ sent: boolean }> {
  return request('/roborock/request-code', { method: 'POST', body: JSON.stringify({ email }) });
}

export function roborockCodeLogin(email: string, code: string): Promise<RoborockDevice[]> {
  return request('/roborock/code-login', { method: 'POST', body: JSON.stringify({ email, code }) });
}

export function roborockGetAccount(): Promise<{ connected: boolean; email?: string; deviceCount?: number }> {
  return request('/roborock/account');
}

// --- Bambu ---

export function bambuAddPrinter(printer: { host: string; serial: string; accessCode: string; model?: string; name: string }): Promise<{ added: string }> {
  return request('/bambu/printers', {
    method: 'POST',
    body: JSON.stringify({ model: '', ...printer }),
  });
}

export function bambuListPrinters(): Promise<BambuPrinter[]> {
  return request('/bambu/printers');
}

export function bambuDeletePrinter(serial: string): Promise<{ removed: string }> {
  return request(`/bambu/printers/${encodeURIComponent(serial)}`, { method: 'DELETE' });
}

