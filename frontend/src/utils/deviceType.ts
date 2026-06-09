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

  if (caps.has('Camera')) return 'Camera';
  if (caps.has('Light')) return 'Light';
  if (caps.has('Cover')) return 'Cover';
  if (caps.has('Switch') && !caps.has('Light')) return 'Switch';
  if (caps.has('Contact')) return 'Contact Sensor';
  if (caps.has('Motion') && !caps.has('Switch') && !caps.has('Light')) return 'Motion Sensor';
  if (caps.has('Action')) return 'Remote';
  if (caps.has('Presence')) return 'Client';

  if (metadata) {
    const features = metadata.features ?? '';
    if (features.includes('switching')) return 'Network Switch';
    if (features.includes('routing')) return 'Network Router';
    if (features.includes('access_point')) return 'Network AccessPoint';
  }

  if ((caps.has('Power') || caps.has('Energy')) && !caps.has('Switch') && !caps.has('Light') && !caps.has('Cover'))
    return 'Power Monitor';
  if ((caps.has('Temperature') || caps.has('Humidity')) && !caps.has('Switch') && !caps.has('Light') && !caps.has('Cover'))
    return 'Sensor';

  return 'Unknown';
}
