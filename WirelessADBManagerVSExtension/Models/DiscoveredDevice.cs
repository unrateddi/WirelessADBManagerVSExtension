namespace WirelessADBManagerVSExtension.Models;

internal class DiscoveredDevice(string ip)
{
    internal string Ip { get; private set; } = ip;
    /// <summary>
    /// The exact serial string ADB uses to identify this device — either an mDNS instance name
    /// (e.g. "adb-XYZ._adb-tls-connect._tcp") or an IP:Port string (e.g. "192.168.1.1:5555").
    /// Set from <c>adb devices</c> output or after an explicit connect operation.
    /// </summary>
    internal string? AdbSerial { get; set; }
    /// <summary>
    /// For USB-connected devices only: the raw USB serial (e.g. "A54J023318002898").
    /// Null for wireless/mDNS devices.
    /// </summary>
    internal string? UsbSerial { get; set; }
    internal int PairingPort { get; set; }
    internal int ConnectPort { get; set; }
    internal string? PairingServiceId { get; set; }
    internal string? ConnectServiceId { get; set; }
    internal DateTime LastPairingAnnouncementTime { get; set; }
    internal DateTime LastManualPairAnnouncementTime { get; set; }
    internal bool IsPaired { get; set; }
    internal bool IsConnected { get; set; }
}
