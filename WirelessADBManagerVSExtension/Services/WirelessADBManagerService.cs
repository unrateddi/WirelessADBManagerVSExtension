using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Security.Cryptography;
using Zeroconf;
using WirelessADBManagerVSExtension.Models;
using System.Runtime.CompilerServices;

namespace WirelessADBManagerVSExtension.Services;

public class WirelessAdbManagerService
{
    #region Constants

    const int KeySize = 5;
    const string ConnectService = "_adb-tls-connect._tcp.local.";
    const string PairingService = "_adb-tls-pairing._tcp.local.";
    const string ModelPlaceholder = "No Model Info";
    const int ServiceBrowseCooldownInMilliseconds = 500;
    const int PairingOverrideThresholdInSeconds = 3;

    #endregion

    #region Statics

    static readonly List<string> s_serviceTypes = [ConnectService, PairingService];
    static readonly char[] s_chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890".ToCharArray();
    static readonly string s_pairingKey = $"ADB_WIFI_{GetUniqueKey(KeySize)}";
    static readonly string s_password = GetUniqueKey(KeySize);

    #endregion

    readonly List<DiscoveredDevice> _discoveredDevices = [];

    internal string GetQrData() => $"WIFI:T:ADB;S:{s_pairingKey};P:{s_password};;";

    internal async IAsyncEnumerable<DeviceInfo> DiscoverDevicesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var discoveredServices = DiscoverServicesAsync(cancellationToken);

        await foreach (var discoveredService in discoveredServices.WithCancellation(cancellationToken))
        {
            var cachedDiscoveredDevice = _discoveredDevices.FirstOrDefault(d => d.Ip == discoveredService.Ip);

            if (cachedDiscoveredDevice?.IsConnected ?? false)
                continue;

            if (discoveredService.ServiceType == $"{s_pairingKey}.{PairingService}")
            {
                if (cachedDiscoveredDevice is null)
                {
                    cachedDiscoveredDevice = new(discoveredService.Ip)
                    {
                        PairingPort = discoveredService.Port,
                        PairingServiceId = discoveredService.ServiceType,
                        Mode = ServiceMode.Pairing,
                        LastPairingAnnouncementTime = discoveredService.AnnouncementTime
                    };

                    _discoveredDevices.Add(cachedDiscoveredDevice);

                    yield return new DeviceInfo
                    {
                        Model = ModelPlaceholder,
                        Ip = discoveredService.Ip,
                        IsConnected = false,
                        IsPaired = false,
                        State = DeviceStates.Pairing
                    };
                }

                if (cachedDiscoveredDevice is not null)
                {
                    cachedDiscoveredDevice.PairingPort = discoveredService.Port;
                    cachedDiscoveredDevice.PairingServiceId = discoveredService.ServiceType;
                    cachedDiscoveredDevice.LastPairingAnnouncementTime = discoveredService.AnnouncementTime;

                    yield return new DeviceInfo
                    {
                        Model = ModelPlaceholder,
                        Ip = discoveredService.Ip,
                        IsConnected = false,
                        IsPaired = false,
                        State = DeviceStates.Pairing
                    };
                }

                if (cachedDiscoveredDevice.ConnectPort <= 0)
                {
                    continue;
                }

                var pairedSuccessfully = await AdbPairAsync(cachedDiscoveredDevice.Ip, cachedDiscoveredDevice.PairingPort, s_password, cancellationToken);

                if (!pairedSuccessfully)
                {
                    continue;
                }

                cachedDiscoveredDevice.IsPaired = true;

                yield return new DeviceInfo
                {
                    Model = ModelPlaceholder,
                    Ip = discoveredService.Ip,
                    IsConnected = false,
                    IsPaired = true,
                    State = DeviceStates.Connecting
                };

                var (Success, ConnectedDevice) = await AdbConnectAsync(cachedDiscoveredDevice.Ip, cachedDiscoveredDevice.ConnectPort, cancellationToken);

                if (Success)
                {
                    yield return new DeviceInfo
                    {
                        Model = ConnectedDevice.Model,
                        Ip = cachedDiscoveredDevice.Ip,
                        IsConnected = true,
                        IsPaired = true,
                        State = DeviceStates.Connected
                    };
                }

                continue;
            }

            if (discoveredService.ServiceType.EndsWith(ConnectService))
            {
                if (cachedDiscoveredDevice is null)
                {
                    cachedDiscoveredDevice = new(discoveredService.Ip)
                    {
                        ConnectPort = discoveredService.Port,
                        ConnectServiceId = discoveredService.ServiceType,
                        Mode = ServiceMode.Connect
                    };

                    _discoveredDevices.Add(cachedDiscoveredDevice);

                    yield return new DeviceInfo
                    {
                        Model = ModelPlaceholder,
                        Ip = discoveredService.Ip,
                        IsConnected = false,
                        IsPaired = false,
                        State = DeviceStates.Disconnected
                    };

                    continue;
                }

                cachedDiscoveredDevice.ConnectPort = discoveredService.Port;
                cachedDiscoveredDevice.ConnectServiceId = discoveredService.ServiceType;

                var state = discoveredService.AnnouncementTime switch
                {
                    _ when cachedDiscoveredDevice.LastPairingAnnouncementTime.AddSeconds(PairingOverrideThresholdInSeconds) > discoveredService.AnnouncementTime => DeviceStates.Pairing,
                    _ when cachedDiscoveredDevice.LastManualPairAnnouncementTime.AddSeconds(PairingOverrideThresholdInSeconds) > discoveredService.AnnouncementTime => DeviceStates.ManualPair,
                    _ => DeviceStates.Disconnected
                };

                yield return new DeviceInfo
                {
                    Model = ModelPlaceholder,
                    Ip = cachedDiscoveredDevice.Ip,
                    IsConnected = false,
                    IsPaired = false,
                    State = state
                };

                continue;
            }

            if (discoveredService.ServiceType.EndsWith(PairingService))
            {
                if (cachedDiscoveredDevice is null)
                {
                    DiscoveredDevice newCachedDiscoveredDevice = new(discoveredService.Ip)
                    {
                        PairingPort = discoveredService.Port,
                        PairingServiceId = discoveredService.ServiceType,
                        Mode = ServiceMode.Pair,
                        LastManualPairAnnouncementTime = discoveredService.AnnouncementTime
                    };

                    _discoveredDevices.Add(newCachedDiscoveredDevice);

                    yield return new DeviceInfo
                    {
                        Model = ModelPlaceholder,
                        Ip = discoveredService.Ip,
                        IsConnected = false,
                        IsPaired = false,
                        State = DeviceStates.ManualPair
                    };

                    continue;
                }

                cachedDiscoveredDevice.PairingPort = discoveredService.Port;
                cachedDiscoveredDevice.PairingServiceId = discoveredService.ServiceType;
                cachedDiscoveredDevice.LastManualPairAnnouncementTime = discoveredService.AnnouncementTime;

                yield return new DeviceInfo
                {
                    Model = ModelPlaceholder,
                    Ip = discoveredService.Ip,
                    IsConnected = false,
                    IsPaired = false,
                    State = DeviceStates.ManualPair
                };
            }
        }
    }

    internal async Task<DeviceInfo> ConnectDeviceAsync(DeviceInfo deviceInfo, CancellationToken cancellationToken)
    {
        var cachedDiscoveredDevice = _discoveredDevices.FirstOrDefault(d => d.Ip == deviceInfo.Ip);

        var (Success, ConnectedDevice) = await AdbConnectAsync(cachedDiscoveredDevice.Ip, cachedDiscoveredDevice.ConnectPort, cancellationToken);

        cachedDiscoveredDevice.IsConnected = Success;

        return new DeviceInfo
        {
            Model = Success ? ConnectedDevice.Model : ModelPlaceholder,
            Ip = cachedDiscoveredDevice.Ip,
            IsConnected = Success,
            IsPaired = Success,
            State = Success ? DeviceStates.Connected : DeviceStates.Disconnected
        };
    }

    internal async Task<DeviceInfo> DisconnectDeviceAsync(DeviceInfo deviceInfo, CancellationToken cancellationToken)
    {
        var cachedDiscoveredDevice = _discoveredDevices.FirstOrDefault(d => d.Ip == deviceInfo.Ip);

        var disconnectedSuccessfully = await AdbDisconnectAsync(cachedDiscoveredDevice.Ip, cachedDiscoveredDevice.ConnectPort, cancellationToken);

        cachedDiscoveredDevice.IsConnected = !disconnectedSuccessfully;

        return new DeviceInfo
        {
            Model = deviceInfo.Model,
            Ip = cachedDiscoveredDevice.Ip,
            IsConnected = !disconnectedSuccessfully,
            IsPaired = disconnectedSuccessfully,
            State = disconnectedSuccessfully ? DeviceStates.Disconnected : DeviceStates.Connected
        };
    }

    internal async Task<DeviceInfo> ManualPairDeviceAsync(DeviceInfo deviceInfo, string password, CancellationToken cancellationToken)
    {
        var cachedDiscoveredDevice = _discoveredDevices.FirstOrDefault(d => d.Ip == deviceInfo.Ip);

        var pairedSuccessfully = await AdbPairAsync(cachedDiscoveredDevice.Ip, cachedDiscoveredDevice.PairingPort, password, cancellationToken);

        if (!pairedSuccessfully)
        {
            return new DeviceInfo
            {
                Model = ModelPlaceholder,
                Ip = cachedDiscoveredDevice.Ip,
                IsConnected = false,
                IsPaired = false,
                State = DeviceStates.ManualPair
            };
        }

        var (Success, ConnectedDevice) = await AdbConnectAsync(cachedDiscoveredDevice.Ip, cachedDiscoveredDevice.ConnectPort, cancellationToken);

        return new DeviceInfo
        {
            Model = Success ? ConnectedDevice.Model : ModelPlaceholder,
            Ip = cachedDiscoveredDevice.Ip,
            IsConnected = Success,
            IsPaired = true,
            State = Success ? DeviceStates.Connected : DeviceStates.ManualPair
        };
    }

    private async IAsyncEnumerable<DiscoveredService> DiscoverServicesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var serviceType in s_serviceTypes)
            {
                IReadOnlyList<IZeroconfHost> results = await ZeroconfResolver.ResolveAsync(serviceType, cancellationToken: cancellationToken);

                foreach (var result in results)
                {
                    foreach (var service in result.Services)
                    {
                        var ipv4Addresses = result.IPAddresses.Where(ip => IPAddress.Parse(ip).AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

                        foreach (var ip in ipv4Addresses)
                        {
                            yield return new(ip, service.Value.Port, service.Key);
                        }
                    }
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(ServiceBrowseCooldownInMilliseconds), cancellationToken);
        }
    }

    #region ADB

    private static async Task<(bool Success, DeviceData ConnectedDevice)> AdbConnectAsync(string ip, int port, CancellationToken cancellationToken)
    {
        await EnsureAdbServerIsRunningAsync(cancellationToken);

        var adbClient = new AdbClient();

        try
        {
            await adbClient.ConnectAsync(ip, port, cancellationToken);
            var devices = await adbClient.GetDevicesAsync();
            var device = devices.FirstOrDefault(d => d.Serial == $"{ip}:{port}");

            if (device == default(DeviceData))
            {
                return (false, new DeviceData());
            }

            return (true, device);
        }
        catch
        {
            return (false, default(DeviceData));
        }
    }

    private static async Task<bool> AdbDisconnectAsync(string ip, int port, CancellationToken cancellationToken)
    {
        await EnsureAdbServerIsRunningAsync(cancellationToken);

        var adbClient = new AdbClient();

        try
        {
            var disconnectResult = await adbClient.DisconnectAsync(ip, port, cancellationToken);

            return !disconnectResult.StartsWith("error");
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> AdbPairAsync(string ip, int port, string password, CancellationToken cancellationToken)
    {
        await EnsureAdbServerIsRunningAsync(cancellationToken);

        var adbClient = new AdbClient();

        try
        {
            var pairingResult = await adbClient.PairAsync(ip, port, password, cancellationToken);

            return !pairingResult.StartsWith("Failed");
        }
        catch
        {
            return false;
        }
    }

    private static async Task EnsureAdbServerIsRunningAsync(CancellationToken cancellationToken)
    {
        if (!AdbServer.Instance.GetStatus().IsRunning)
        {
            AdbServer server = new();
            StartServerResult result = await server.StartServerAsync("adb", false, cancellationToken);
            if (result != StartServerResult.Started)
            {
                Console.WriteLine("Can't start adb server");
            }
        }
    }

    #endregion

    #region Helpers

    private static string GetUniqueKey(int size)
    {
        byte[] data = new byte[4 * size];
        using (var crypto = RandomNumberGenerator.Create())
        {
            crypto.GetBytes(data);
        }
        StringBuilder result = new(size);
        for (int i = 0; i < size; i++)
        {
            var rnd = BitConverter.ToUInt32(data, i * 4);
            var idx = rnd % s_chars.Length;

            result.Append(s_chars[idx]);
        }

        return result.ToString();
    }

    #endregion
}
