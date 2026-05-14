#pragma warning disable VSEXTPREVIEW_SETTINGS // The settings API is currently in preview and marked as experimental

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace WirelessADBManagerVSExtension;

internal static class SettingDefinitions
{
    [VisualStudioContribution]
    internal static SettingCategory WirelessADBManagerCategory { get; } = new("wirelessADBManagerCategory", "%WirelessADBManagerVSExtension.SettingDefinitions.Category%")
    {
        GenerateObserverClass = true
    };

    [VisualStudioContribution]
    internal static Setting.String ADBPath { get; } = new("adbPath", "%WirelessADBManagerVSExtension.SettingDefinitions.ADBPath%", WirelessADBManagerCategory, defaultValue: string.Empty);

    [VisualStudioContribution]
    internal static Setting.Integer TcpIpPort { get; } = new("tcpIpPort", "%WirelessADBManagerVSExtension.SettingDefinitions.TcpIpPort%", WirelessADBManagerCategory, defaultValue: 5555);

    [VisualStudioContribution]
    internal static Setting.Boolean TcpIpUseRandomPort { get; } = new("tcpIpUseRandomPort", "%WirelessADBManagerVSExtension.SettingDefinitions.TcpIpUseRandomPort%", WirelessADBManagerCategory, defaultValue: false);
}