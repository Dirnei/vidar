export type DeviceType =
  | 'Camera'
  | 'Light'
  | 'Cover'
  | 'Switch'
  | 'Sensor'
  | 'Motion Sensor'
  | 'Contact Sensor'
  | 'Remote'
  | 'Client'
  | 'Power Monitor'
  | 'Network Switch'
  | 'Network Router'
  | 'Network AccessPoint'
  | 'Unknown';

export function deriveDeviceType(capabilities: string[], metadata?: Record<string, string>): DeviceType {
  const caps = new Set(capabilities);

  if (caps.has('camera')) return 'Camera';
  if (caps.has('light')) return 'Light';
  if (caps.has('cover')) return 'Cover';
  if (caps.has('switch') && !caps.has('light')) return 'Switch';
  if (caps.has('contact')) return 'Contact Sensor';
  if (caps.has('motion') && !caps.has('switch') && !caps.has('light')) return 'Motion Sensor';
  if (caps.has('action')) return 'Remote';
  if (caps.has('presence')) return 'Client';

  if (metadata) {
    const features = metadata.features ?? '';
    if (features.includes('switching')) return 'Network Switch';
    if (features.includes('routing')) return 'Network Router';
    if (features.includes('access_point')) return 'Network AccessPoint';
  }

  if ((caps.has('power') || caps.has('energy')) && !caps.has('switch') && !caps.has('light') && !caps.has('cover'))
    return 'Power Monitor';
  if ((caps.has('temperature') || caps.has('humidity')) && !caps.has('switch') && !caps.has('light') && !caps.has('cover'))
    return 'Sensor';

  return 'Unknown';
}
