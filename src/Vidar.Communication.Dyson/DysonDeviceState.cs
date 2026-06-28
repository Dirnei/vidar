namespace Vidar.Communication.Dyson;

// Connection state surfaced as the device's "transport" status. With the dyson2mqtt sidecar the
// worker only reports Local (consuming the broker) or Offline (broker unreachable); the other
// values remain for status-string compatibility with the frontend.
public enum DysonTransport { Local, NeedsConnection, Offline, NeedsReauth }
