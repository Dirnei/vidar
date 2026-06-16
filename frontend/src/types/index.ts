export interface Room {
  id: string;
  name: string;
  isHome?: boolean;
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
  | 'Humidity'
  | 'Light'
  | 'Contact'
  | 'Action'
  | 'Battery'
  | 'Presence'
  | 'Camera'
  | 'Update'
  | 'Extras';

export interface StateHistoryEntry {
  capability: string;
  value: unknown;
  timestamp: string;
}

export interface CommandHistoryEntry {
  capability: string;
  value: unknown;
  source: string | null;
  timestamp: string;
}

export interface Application {
  id: string;
  name: string;
  type: 'provider' | 'consumer';
  enabled: boolean;
  status: 'running' | 'stopped' | 'error' | 'unconfigured';
  deviceCount: number;
  settings: Record<string, string>;
  errorMessage: string | null;
}

export interface WebhookRoute {
  routeKey: string;
  integrationId: string | null;
  authMode: 'None' | 'UrlSecret' | 'HeaderToken';
  path: string;
  headerName: string | null;
}

export interface FilterSection {
  key: string;
  label: string;
  options: FilterOption[];
}

export interface FilterOption {
  value: string;
  label: string;
  count: number;
}

export type ActiveFilters = Record<string, Set<string>>;

export interface WebhookEvent {
  payloadId: string;
  routeKey: string;
  integrationId: string | null;
  contentType: string;
  contentLength: number;
  receivedAt: string;
  status: 'pending' | 'handled' | 'failed';
  handledAt: string | null;
  error: string | null;
}

export interface WebhookEventPage {
  items: WebhookEvent[];
  totalCount: number;
}

export interface WebhookHandledEvent {
  payloadId: string;
  status: 'handled' | 'failed';
  error: string | null;
  handledAt: string;
}
