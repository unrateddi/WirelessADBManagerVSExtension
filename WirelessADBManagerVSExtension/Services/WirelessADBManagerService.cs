using AdvancedSharpAdbClient.Models;
using System.Net;
using Zeroconf;
using WirelessADBManagerVSExtension.Models;
using System.Runtime.CompilerServices;
using WirelessADBManagerVSExtension.Utils;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace WirelessADBManagerVSExtension.Services;

public class WirelessAdbManagerService()
{
    #region Constants

    const int KeySize = 5;
    const string ConnectService = "_adb-tls-connect._tcp.local.";
    const string PairingService = "_adb-tls-pairing._tcp.local.";
    const string ModelPlaceholder = "No Model Info";
    const int ServiceBrowseCooldownInMilliseconds = 500;
    const int PairingOverrideThresholdInSeconds = 3;
    const char IpPortSeparator = ':';

    #endregion

    #region Statics

    static readonly List<string> s_serviceTypes = [ConnectService, PairingService];
    static readonly string s_pairingKey = $"ADB_WIFI_{KeyGenerator.GetUniqueKey(KeySize)}";
    static readonly string s_password = KeyGenerator.GetUniqueKey(KeySize);
    internal static readonly string QrData = $"WIFI:T:ADB;S:{s_pairingKey};P:{s_password};;";

    #endregion

    readonly AdbService _adbService = new();
    readonly List<DiscoveredDevice> _discoveredDevices = [];

    internal async IAsyncEnumerable<DeviceInfo> DiscoverDevicesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var adbDevices = await _adbService.AdbListDevicesAsync(cancellationToken);

        foreach (var device in adbDevices)
        {
            string ip = string.Empty;
            int port = 0;

            try
            {
                (ip, port) = ParseIpAndPortFromSerial(device.Serial);
            }
            catch
            {
                continue;
            }

            var cachedDiscoveredDevice = new DiscoveredDevice(ip)
            {
                ConnectPort = port,
                IsPaired = true,
                IsConnected = device.State == DeviceState.Online
            };

            _discoveredDevices.Add(cachedDiscoveredDevice);

            yield return new()
            {
                Ip = cachedDiscoveredDevice.Ip,
                IsPaired = cachedDiscoveredDevice.IsPaired,
                IsConnected = cachedDiscoveredDevice.IsConnected,
                Model = device.Model,
                State = cachedDiscoveredDevice.IsConnected ? DeviceStates.Connected : DeviceStates.Disconnected
            };
        }

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

                var pairedSuccessfully = await _adbService.AdbPairAsync(cachedDiscoveredDevice.Ip, cachedDiscoveredDevice.PairingPort, s_password, cancellationToken);

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

                var (Success, ConnectedDevice) = await _adbService.AdbConnectAsync(cachedDiscoveredDevice.Ip, cachedDiscoveredDevice.ConnectPort, cancellationToken);

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

        var (Success, ConnectedDevice) = await _adbService.AdbConnectAsync(cachedDiscoveredDevice.Ip, cachedDiscoveredDevice.ConnectPort, cancellationToken);

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

        var disconnectedSuccessfully = await _adbService.AdbDisconnectAsync(cachedDiscoveredDevice.Ip, cachedDiscoveredDevice.ConnectPort, cancellationToken);

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

        var pairedSuccessfully = await _adbService.AdbPairAsync(cachedDiscoveredDevice.Ip, cachedDiscoveredDevice.PairingPort, password, cancellationToken);

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

        var (Success, ConnectedDevice) = await _adbService.AdbConnectAsync(cachedDiscoveredDevice.Ip, cachedDiscoveredDevice.ConnectPort, cancellationToken);

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

    #region Helpers

    private static (string Ip, int Port) ParseIpAndPortFromSerial(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial) || !serial.Contains(IpPortSeparator))
            throw new ArgumentException("Input parameter is not in the correct format", nameof(serial));

        var ipAndPortSplitted = serial.Split(IpPortSeparator);

        var ip = ipAndPortSplitted[0];
        var port = int.Parse(ipAndPortSplitted[1]);

        return (ip, port);
    }

    #endregion
}
