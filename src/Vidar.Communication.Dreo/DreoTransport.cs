namespace Vidar.Communication.Dreo;

// With the dreo2mqtt sidecar the worker only reports Local (consuming the broker)
// or Offline (broker unreachable); the cloud connection lives entirely in the sidecar.
public enum DreoTransport { Local, Offline }
