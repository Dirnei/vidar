export interface Room {
  id: string;
  name: string;
}

export interface Device {
  id: string;
  name: string;
  roomId: string | null;
  communicationType: string;
  capabilities: string[];
  state: Record<string, unknown>;
  metadata: Record<string, string>;
  online?: boolean;
  settings?: Record<string, string>;
  groupId?: string;
  groupName?: string;
}

export interface DeviceGroup {
  id: string;
  name: string;
  roomId: string;
  roomName: string | null;
  deviceIds: string[];
  capabilities: string[];
  state: Record<string, unknown> | null;
  online: boolean | null;
}

export interface DiscoveredDevice {
  id: string;
  nativeId: string;
  communicationType: string;
  capabilities: string[];
  metadata: Record<string, string>;
}

export interface CommandPayload {
  capability: string;
  value: unknown;
}

export interface ConfigurePayload {
  name: string;
  roomId: string;
}

export type Capability =
  | 'Switch'
  | 'Dimmer'
  | 'Cover'
  | 'Temperature'
  | 'Motion'
  | 'Power'
  | 'Energy'
  | 'Humidity';
