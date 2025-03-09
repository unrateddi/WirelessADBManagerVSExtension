using System;

namespace WirelessADBManagerVSExtension.Models;

internal class DiscoveredDevice(string ip)
{
    internal string Ip { get; private set; } = ip;
    internal int PairingPort { get; set; }
    internal int ConnectPort { get; set; }
    internal int ManualPairPort { get; set; }
    internal string PairingServiceId { get; set; }
    internal string ConnectServiceId { get; set; }
    internal string ManualPairServiceId { get; set; }
    internal ServiceMode Mode { get; set; }
    internal DateTime LastPairingAnnouncementTime { get; set; }
    internal DateTime LastManualPairAnnouncementTime { get; set; }
}
