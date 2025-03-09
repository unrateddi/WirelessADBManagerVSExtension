using System;

namespace WirelessADBManagerVSExtension.Models;

internal record DiscoveredService
{
    internal string Ip { get; private set; }
    internal int Port { get; private set; }
    internal string ServiceType { get; private set; }
    internal DateTime AnnouncementTime { get; } = DateTime.Now;

    internal DiscoveredService(string ip, int port, string serviceType)
    {
        Ip = ip;
        Port = port;
        ServiceType = serviceType;
    }
}
