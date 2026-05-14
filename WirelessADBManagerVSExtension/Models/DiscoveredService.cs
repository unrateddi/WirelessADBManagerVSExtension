namespace WirelessADBManagerVSExtension.Models;

internal record DiscoveredService(string Ip, int Port, string ServiceType)
{
    internal DateTime AnnouncementTime { get; } = DateTime.Now;
}
