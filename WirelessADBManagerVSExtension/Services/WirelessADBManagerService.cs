using AdvancedSharpAdbClient.Models;
using Makaretu.Dns;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using WirelessADBManagerVSExtension.Models;
using WirelessADBManagerVSExtension.Utils;

namespace WirelessADBManagerVSExtension.Services;

public sealed class WirelessAdbManagerService(AdbService _adbService) : IDisposable
{
    #region Constants

    const string ConnectService = "_adb-tls-connect._tcp.local.";
    const string PairingService = "_adb-tls-pairing._tcp.local.";
    const string ModelPlaceholder = "No Model Info";
    const int PairingKeySize = 5;
    const int PasswordKeySize = 12;
    const int AdbOperationTimeoutSeconds = 10;
    // How long a pairing-service announcement is trusted as "still offered" before falling back to Disconnected.
    // Generous on purpose: phones re-announce periodically and shutdown events for pairing services are unreliable.
    static readonly TimeSpan PairingFreshnessWindow = TimeSpan.FromSeconds(20);

    #endregion

    #region Fields

    readonly AdbService _adbService = _adbService;
    readonly Dictionary<string, DiscoveredDevice> _devices = [];
    readonly object _devicesLock = new();
    readonly string _pairingKey = $"ADB_WIFI_{KeyGenerator.GetUniqueKey(PairingKeySize)}";
    readonly string _password = KeyGenerator.GetUniqueKey(PasswordKeySize);
    internal string QrData => $"WIFI:T:ADB;S:{_pairingKey};P:{_password};;";

    // VS-lifetime passive mDNS listener — started once, runs until VS closes.
    private MulticastService? _multicast;
    private ServiceDiscovery? _sd;
    private readonly object _listenerLock = new();
    private bool _listenerStarted;

    // Per-window-session subscriber channels — one channel writer per open tool window.
    private readonly List<ChannelWriter<DiscoveredService>> _subscribers = [];
    private readonly object _subscribersLock = new();

    #endregion

    internal async IAsyncEnumerable<DeviceInfo> DiscoverDevicesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        lock (_devicesLock)
            _devices.Clear();

        // Start the background mDNS listener (idempotent — runs for the VS session lifetime).
        StartListenerIfNeeded();

        // Create a fresh channel for this window session. The background listener will
        // broadcast discovered services into it. Unbounded so no announcements are dropped.
        var sessionChannel = Channel.CreateUnbounded<DiscoveredService>(
            new UnboundedChannelOptions { SingleWriter = false, SingleReader = true });

        lock (_subscribersLock)
            _subscribers.Add(sessionChannel.Writer);

        try
        {
            // Phase 1 — yield devices ADB already knows about (instant, no network query).
            await foreach (var info in EnumerateKnownAdbDevicesAsync(cancellationToken))
                yield return info;

            // Phase 2 — trigger an active mDNS PTR query so nearby devices respond immediately.
            // Responses arrive asynchronously through the passive listener and feed the channel.
            _sd!.QueryServiceInstances(new DomainName("_adb-tls-connect._tcp"));
            _sd!.QueryServiceInstances(new DomainName("_adb-tls-pairing._tcp"));

            // Phase 3 — stream all subsequent passive discoveries until the window is closed.
            await foreach (var service in sessionChannel.Reader.ReadAllAsync(cancellationToken))
            {
                DiscoveredDevice? cached;
                lock (_devicesLock) { _devices.TryGetValue(service.Ip, out cached); }

                if (cached?.IsConnected ?? false)
                    continue;

                switch (ClassifyServiceType(service.ServiceType))
                {
                    case ServiceClass.QrPairing:
                        await foreach (var info in HandlePairingServiceAsync(cached, service, cancellationToken))
                            yield return info;
                        break;

                    case ServiceClass.Connect:
                        if (HandleConnectService(cached, service) is { } connectInfo)
                            yield return connectInfo;
                        break;

                    case ServiceClass.ManualPairing:
                        if (HandleManualPairingService(cached, service) is { } pairInfo)
                            yield return pairInfo;
                        break;
                }
            }
        }
        finally
        {
            lock (_subscribersLock)
                _subscribers.Remove(sessionChannel.Writer);
            sessionChannel.Writer.TryComplete();
        }
    }

    #region Passive mDNS listener

    private void StartListenerIfNeeded()
    {
        if (_listenerStarted) return;

        lock (_listenerLock)
        {
            if (_listenerStarted) return;

            _multicast = new MulticastService();
            _sd = new ServiceDiscovery(_multicast);

            _sd.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;
            _sd.ServiceInstanceShutdown += OnServiceInstanceShutdown;

            _multicast.Start();
            _listenerStarted = true;
        }
    }

    private void OnServiceInstanceDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs e)
    {
        // The event message should contain SRV + A records in Answers or AdditionalRecords.
        var allRecords = e.Message.Answers.Concat(e.Message.AdditionalRecords);

        var srv = allRecords.OfType<SRVRecord>()
            .FirstOrDefault(r => r.Name == e.ServiceInstanceName);
        if (srv is null) return;

        var aRecord = allRecords.OfType<ARecord>()
            .FirstOrDefault(r => r.Name == srv.Target);
        if (aRecord is null) return;

        // IPv4 only — mirrors the original IsIPv4Address filter.
        if (aRecord.Address.AddressFamily != AddressFamily.InterNetwork) return;

        var ip = aRecord.Address.ToString();
        var port = srv.Port;
        var serviceType = InstanceNameToTypeString(e.ServiceInstanceName);

        if (string.IsNullOrEmpty(serviceType)) return;

        BroadcastService(new DiscoveredService(ip, port, serviceType));
    }

    private void OnServiceInstanceShutdown(object? sender, ServiceInstanceShutdownEventArgs e)
    {
        var instanceId = InstanceNameToTypeString(e.ServiceInstanceName);

        // Pairing-service shutdown notifications are unreliable (phones fire them routinely even while
        // still offering pairing), so only a connect-service shutdown — meaning the device truly went
        // offline — evicts it. Pairing activity is tracked separately via a freshness window instead.
        if (ClassifyServiceType(instanceId) != ServiceClass.Connect)
            return;

        lock (_devicesLock)
        {
            var entry = _devices.Values.FirstOrDefault(d => d.ConnectServiceId == instanceId);
            if (entry is not null)
                _devices.Remove(entry.Ip);
        }
    }

    private void BroadcastService(DiscoveredService service)
    {
        lock (_subscribersLock)
            foreach (var writer in _subscribers)
                writer.TryWrite(service);
    }

    // Converts a fully-qualified mDNS instance name DomainName to the string format used
    // internally (e.g. "ADB_WIFI_XYZ._adb-tls-pairing._tcp.local." with trailing dot).
    // DomainName.ToString() includes the trailing dot on most Makaretu versions; we ensure it.
    private static string InstanceNameToTypeString(DomainName instanceName)
    {
        var s = instanceName.ToString();
        return s.EndsWith('.') ? s : s + ".";
    }

    #endregion

    #region Discovery pipeline

    private async IAsyncEnumerable<DeviceInfo> EnumerateKnownAdbDevicesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IEnumerable<DeviceData> adbDevices;
        try
        {
            adbDevices = await _adbService.AdbListDevicesAsync(cancellationToken);
        }
        catch (OperationCanceledException) { yield break; }

        foreach (var device in adbDevices)
        {
            (string ip, int port) address;
            try
            {
                address = ParseIpAndPortFromSerial(device.Serial);
            }
            catch
            {
                var resolved = await TryResolveMdnsSerialAsync(device.Serial, cancellationToken);
                if (resolved is null) continue;
                address = resolved.Value;
            }

            lock (_devicesLock)
            {
                if (_devices.ContainsKey(address.ip)) continue;
            }

            var cached = new DiscoveredDevice(address.ip)
            {
                ConnectPort = address.port,
                AdbSerial = device.Serial,   // Preserve the exact serial ADB reported
                IsPaired = true,
                IsConnected = device.State == DeviceState.Online
            };

            lock (_devicesLock) { _devices[address.ip] = cached; }

            yield return new DeviceInfo
            {
                Ip = cached.Ip,
                AdbSerial = cached.AdbSerial,
                Model = device.Model,
                IsPaired = true,
                IsConnected = cached.IsConnected,
                State = cached.IsConnected ? DeviceStates.Connected : DeviceStates.Disconnected
            };
        }
    }

    private async IAsyncEnumerable<DeviceInfo> HandlePairingServiceAsync(
        DiscoveredDevice? cached,
        DiscoveredService service,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cached is null)
        {
            cached = new(service.Ip)
            {
                PairingPort = service.Port,
                PairingServiceId = service.ServiceType,
                LastQrPairingSeenUtc = DateTime.UtcNow
            };
            lock (_devicesLock) { _devices[service.Ip] = cached; }
        }
        else
        {
            lock (_devicesLock)
            {
                cached.PairingPort = service.Port;
                cached.PairingServiceId = service.ServiceType;
                cached.LastQrPairingSeenUtc = DateTime.UtcNow;
            }
        }

        yield return new DeviceInfo
        {
            Model = ModelPlaceholder,
            Ip = service.Ip,
            IsConnected = false,
            IsPaired = false,
            State = DeviceStates.Pairing
        };

        if (cached.ConnectPort <= 0)
            yield break;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(AdbOperationTimeoutSeconds));

        var pairResult = await PairWithTimeoutHandlingAsync(cached.Ip, cached.PairingPort, _password, cts, cancellationToken);

        if (!pairResult.Success)
        {
            // QR pairing failed, so return to the normal manual-pair action instead of showing an active pairing state.
            yield return new DeviceInfo
            {
                Model = ModelPlaceholder,
                Ip = service.Ip,
                IsConnected = false,
                IsPaired = false,
                State = DeviceStates.ManualPair,
                FailureReason = BuildQrPairingFailureMessage(pairResult.Message)
            };
            yield break;
        }

        lock (_devicesLock) { cached.IsPaired = true; }

        yield return new DeviceInfo
        {
            Model = ModelPlaceholder,
            Ip = service.Ip,
            IsConnected = false,
            IsPaired = true,
            State = DeviceStates.Connecting
        };

        var connectResult = await ConnectWithTimeoutHandlingAsync(cached.Ip, cached.ConnectPort, cts, cancellationToken);

        if (!connectResult.Success || connectResult.ConnectedDevice is null)
        {
            yield return new DeviceInfo
            {
                Model = ModelPlaceholder,
                Ip = service.Ip,
                IsConnected = false,
                IsPaired = true,
                State = DeviceStates.ConnectionFailed,
                FailureReason = BuildFailureMessage(connectResult.FailureKind, connectResult.Message)
            };
            yield break;
        }

        lock (_devicesLock)
        {
            cached.IsConnected = true;
            cached.LastQrPairingSeenUtc = DateTime.MinValue;
            // After an explicit IP:Port connect the ADB serial is always "IP:Port".
            cached.AdbSerial = $"{cached.Ip}:{cached.ConnectPort}";
        }

        yield return new DeviceInfo
        {
            Model = connectResult.ConnectedDevice.Model,
            Ip = cached.Ip,
            AdbSerial = cached.AdbSerial,
            IsConnected = true,
            IsPaired = true,
            State = DeviceStates.Connected
        };
    }

    private DeviceInfo? HandleConnectService(DiscoveredDevice? cached, DiscoveredService service)
    {
        if (cached is null)
        {
            // The connect service is only advertised once a device has been paired, so a device
            // first seen through it must be treated as previously paired even though our
            // per-window cache (cleared in DiscoverDevicesAsync) has no record of it — otherwise
            // an offline-but-paired phone would lose its reconnect action.
            var newDevice = new DiscoveredDevice(service.Ip)
            {
                ConnectPort = service.Port,
                ConnectServiceId = service.ServiceType,
                IsPaired = true
            };
            lock (_devicesLock) { _devices[service.Ip] = newDevice; }

            return new DeviceInfo
            {
                Model = ModelPlaceholder,
                Ip = service.Ip,
                IsConnected = false,
                IsPaired = true,
                State = DeviceStates.Disconnected
            };
        }

        lock (_devicesLock)
        {
            cached.ConnectPort = service.Port;
            cached.ConnectServiceId = service.ServiceType;
        }

        // The phone keeps re-announcing the connect service on mDNS on its own schedule.
        // If we're already connected/paired (e.g. QR auto-connect just completed), this
        // re-announcement carries no new information — don't clobber the connected UI state.
        if (cached.IsConnected)
            return null;

        // A pairing offer seen within the freshness window takes precedence over the connect
        // service's own re-announcements, even if the exact announcement timings don't line up.
        // A device that has never been paired has no reconnect option available either — only a
        // previously-paired device can meaningfully retry "Connect".
        var now = DateTime.UtcNow;
        var state = now switch
        {
            _ when now - cached.LastQrPairingSeenUtc < PairingFreshnessWindow => DeviceStates.Pairing,
            _ when now - cached.LastManualPairingSeenUtc < PairingFreshnessWindow => DeviceStates.ManualPair,
            _ when cached.IsPaired => DeviceStates.Disconnected,
            _ => DeviceStates.NotPaired
        };

        return new DeviceInfo
        {
            Model = ModelPlaceholder,
            Ip = cached.Ip,
            IsConnected = false,
            IsPaired = cached.IsPaired,
            State = state
        };
    }

    private DeviceInfo? HandleManualPairingService(DiscoveredDevice? cached, DiscoveredService service)
    {
        if (cached is null)
        {
            var newDevice = new DiscoveredDevice(service.Ip)
            {
                PairingPort = service.Port,
                PairingServiceId = service.ServiceType,
                LastManualPairingSeenUtc = DateTime.UtcNow
            };
            lock (_devicesLock) { _devices[service.Ip] = newDevice; }
        }
        else
        {
            lock (_devicesLock)
            {
                cached.PairingPort = service.Port;
                cached.PairingServiceId = service.ServiceType;
                cached.LastManualPairingSeenUtc = DateTime.UtcNow;
            }
        }

        return new DeviceInfo
        {
            Model = ModelPlaceholder,
            Ip = service.Ip,
            IsConnected = false,
            IsPaired = false,
            State = DeviceStates.ManualPair
        };
    }

    #endregion

    internal async Task<DeviceInfo> ConnectDeviceAsync(DeviceInfo deviceInfo, CancellationToken cancellationToken)
    {
        DiscoveredDevice? cached;
        lock (_devicesLock) { _devices.TryGetValue(deviceInfo.Ip, out cached); }

        if (cached is null)
            return new DeviceInfo { Model = deviceInfo.Model, Ip = deviceInfo.Ip, AdbSerial = deviceInfo.AdbSerial, IsConnected = false, IsPaired = false, State = DeviceStates.Disconnected };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(AdbOperationTimeoutSeconds));

        var connectResult = await ConnectWithTimeoutHandlingAsync(cached.Ip, cached.ConnectPort, cts, cancellationToken);

        lock (_devicesLock)
        {
            cached.IsConnected = connectResult.Success;
            // Explicit IP:Port connect — ADB always tracks it as "IP:Port" afterwards.
            if (connectResult.Success) cached.AdbSerial = $"{cached.Ip}:{cached.ConnectPort}";
        }

        return new DeviceInfo
        {
            Model = connectResult.Success && connectResult.ConnectedDevice is not null ? connectResult.ConnectedDevice.Model : deviceInfo.Model,
            Ip = cached.Ip,
            AdbSerial = cached.AdbSerial,
            IsConnected = connectResult.Success,
            IsPaired = cached.IsPaired,
            State = connectResult.Success ? DeviceStates.Connected : DeviceStates.ConnectionFailed,
            FailureReason = connectResult.Success ? null : BuildFailureMessage(connectResult.FailureKind, connectResult.Message)
        };
    }

    internal async Task<DeviceInfo> DisconnectDeviceAsync(DeviceInfo deviceInfo, CancellationToken cancellationToken)
    {
        DiscoveredDevice? cached;
        lock (_devicesLock) { _devices.TryGetValue(deviceInfo.Ip, out cached); }

        if (cached is null)
            return new DeviceInfo { Model = deviceInfo.Model, Ip = deviceInfo.Ip, AdbSerial = deviceInfo.AdbSerial, IsConnected = false, IsPaired = false, State = DeviceStates.Disconnected };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(AdbOperationTimeoutSeconds));

        // Prefer the exact ADB serial for disconnect — it handles both mDNS instance names
        // (auto-connected devices) and legacy IP:Port serials transparently.
        // Fall back to IP:Port construction only if no serial has been recorded yet.
        bool disconnectedSuccessfully;
        var serial = cached.AdbSerial;
        if (serial is not null)
        {
            disconnectedSuccessfully = await _adbService.AdbDisconnectBySerialAsync(serial, cts.Token);
        }
        else if (cached.ConnectPort > 0)
        {
            disconnectedSuccessfully = await _adbService.AdbDisconnectAsync(cached.Ip, cached.ConnectPort, cts.Token);
        }
        else
        {
            disconnectedSuccessfully = false;
        }

        lock (_devicesLock) { cached.IsConnected = !disconnectedSuccessfully; }

        return new DeviceInfo
        {
            Model = deviceInfo.Model,
            Ip = cached.Ip,
            AdbSerial = cached.AdbSerial,
            IsConnected = !disconnectedSuccessfully,
            IsPaired = cached.IsPaired,
            State = disconnectedSuccessfully ? DeviceStates.Disconnected : DeviceStates.Connected
        };
    }

    internal async Task<DeviceInfo> ManualPairDeviceAsync(DeviceInfo deviceInfo, string password, CancellationToken cancellationToken)
    {
        DiscoveredDevice? cached;
        lock (_devicesLock) { _devices.TryGetValue(deviceInfo.Ip, out cached); }

        if (cached is null)
            return new DeviceInfo { Model = deviceInfo.Model, Ip = deviceInfo.Ip, IsConnected = false, IsPaired = false, State = DeviceStates.ManualPair };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(AdbOperationTimeoutSeconds));

        var pairResult = await PairWithTimeoutHandlingAsync(cached.Ip, cached.PairingPort, password, cts, cancellationToken);

        if (!pairResult.Success)
            // Reset back to the actionable "Pair" state so the user can simply retry with a new code.
            return new DeviceInfo
            {
                Model = deviceInfo.Model,
                Ip = cached.Ip,
                IsConnected = false,
                IsPaired = false,
                State = DeviceStates.ManualPair,
                FailureReason = BuildFailureMessage(pairResult.FailureKind, pairResult.Message)
            };

        lock (_devicesLock) { cached.IsPaired = true; }

        var connectResult = await ConnectWithTimeoutHandlingAsync(cached.Ip, cached.ConnectPort, cts, cancellationToken);

        lock (_devicesLock)
        {
            cached.IsConnected = connectResult.Success;
            if (connectResult.Success) cached.AdbSerial = $"{cached.Ip}:{cached.ConnectPort}";
        }

        return new DeviceInfo
        {
            Model = connectResult.Success && connectResult.ConnectedDevice is not null ? connectResult.ConnectedDevice.Model : deviceInfo.Model,
            Ip = cached.Ip,
            AdbSerial = cached.AdbSerial,
            IsConnected = connectResult.Success,
            IsPaired = true,
            State = connectResult.Success ? DeviceStates.Connected : DeviceStates.ConnectionFailed,
            FailureReason = connectResult.Success ? null : BuildFailureMessage(connectResult.FailureKind, connectResult.Message)
        };
    }

    /// <summary>
    /// Streams USB-connected (non-wireless) devices via ADB track-devices. Each emission
    /// reflects the current full USB device snapshot. Runs until cancellation.
    /// </summary>
    internal async IAsyncEnumerable<IReadOnlyList<DeviceInfo>> TrackUsbDevicesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var snapshot in _adbService.TrackDevicesAsync(cancellationToken))
        {
            var usbDevices = snapshot.Where(IsUsbDevice).ToList();

            // Fetch model and wireless ADB state for each device in parallel.
            var modelTasks = usbDevices.Select(d =>
                _adbService.GetModelAsync(d.Serial, cancellationToken)).ToList();
            var wirelessPortTasks = usbDevices.Select(d =>
                _adbService.GetWirelessAdbPortAsync(d.Serial, cancellationToken)).ToList();

            await Task.WhenAll(modelTasks.Cast<Task>().Concat(wirelessPortTasks));

            var models = modelTasks.Select(t => t.Result).ToArray();
            var wirelessPorts = wirelessPortTasks.Select(t => t.Result).ToArray();

            var deviceInfos = usbDevices.Select((d, i) => new DeviceInfo
            {
                Model = string.IsNullOrEmpty(models[i]) ? ModelPlaceholder : models[i]!,
                Ip = d.Serial, // USB devices have no IP — use serial as unique key in the list
                UsbSerial = d.Serial,
                AdbSerial = d.Serial,
                IsConnected = false,
                IsPaired = false,
                State = DeviceStates.UsbTcpip,
                IsWirelessAdbEnabled = wirelessPorts[i] > 0
            }).ToList();

            yield return deviceInfos;
        }
    }

    /// <summary>
    /// Enables TCP/IP wireless ADB mode on a USB-connected device. Attempts to auto-detect
    /// the device's WiFi IP. Falls back to <paramref name="manualIpFallback"/> if auto-detect
    /// fails. Returns the new wireless <see cref="DeviceInfo"/> on success.
    /// </summary>
    internal async Task<DeviceInfo?> EnableTcpIpModeAsync(
        DeviceInfo usbDeviceInfo,
        string deviceIp,
        int port,
        CancellationToken cancellationToken)
    {
        var usbSerial = usbDeviceInfo.UsbSerial;
        if (string.IsNullOrEmpty(usbSerial))
            return null;

        // The caller (ViewModel) already resolved the WiFi IP — either auto-detected or
        // entered manually by the user. No need to resolve it again here.
        if (string.IsNullOrEmpty(deviceIp))
            return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(AdbOperationTimeoutSeconds * 3)); // 30 s: tcpip + connect

        var (success, connectedDevice) = await _adbService.EnableTcpIpModeAsync(usbSerial, deviceIp, port, cts.Token);

        if (!success || connectedDevice is null)
            return null;

        // Register the newly wireless device in the cache.
        var cached = new DiscoveredDevice(deviceIp)
        {
            ConnectPort = port,
            AdbSerial = $"{deviceIp}:{port}",
            UsbSerial = usbSerial,
            IsPaired = true,
            IsConnected = true
        };
        lock (_devicesLock) { _devices[deviceIp] = cached; }

        return new DeviceInfo
        {
            Model = string.IsNullOrEmpty(connectedDevice.Model) ? usbDeviceInfo.Model : connectedDevice.Model,
            Ip = deviceIp,
            AdbSerial = cached.AdbSerial,
            IsConnected = true,
            IsPaired = true,
            State = DeviceStates.Connected,
            IsWirelessAdbEnabled = true  // we just enabled tcpip mode
        };
    }

    /// <summary>Turns off wireless ADB on the given device (USB or wireless transport).</summary>
    internal Task<bool> DisableWirelessAdbAsync(string serial, CancellationToken cancellationToken)
        => _adbService.DisableWirelessAdbAsync(serial, cancellationToken);

    private static bool IsUsbDevice(DeviceData device)
    {
        var s = device.Serial;
        // USB serials are alphanumeric with no colons, dots, or "adb-"/"emulator-" prefix.
        return !string.IsNullOrEmpty(s)
            && !s.Contains(':')
            && !s.Contains('.')
            && !s.StartsWith("adb-", StringComparison.Ordinal)
            && !s.StartsWith("emulator-", StringComparison.Ordinal);
    }

    /// <summary>Thin async wrapper so the ViewModel can pre-check the WiFi IP before prompting.</summary>
    internal Task<string?> TryGetWifiIpForUsbDeviceAsync(string usbSerial, CancellationToken cancellationToken)
        => _adbService.GetWifiIpAsync(usbSerial, cancellationToken);

    /// <summary>Returns the configured TCP/IP port (or a random ephemeral port).</summary>
    internal Task<int> GetTcpIpPortAsync(CancellationToken cancellationToken)
        => _adbService.GetTcpIpPortAsync(cancellationToken);

    #region Failure handling

    /// <summary>
    /// Runs <see cref="AdbService.AdbPairAsync"/> and tells apart the operation's own timeout
    /// (reported as a network failure) from an external cancellation (propagated as-is).
    /// </summary>
    private async Task<AdbPairResult> PairWithTimeoutHandlingAsync(
        string ip, int port, string password, CancellationTokenSource cts, CancellationToken callerToken)
    {
        try
        {
            return await _adbService.AdbPairAsync(ip, port, password, cts.Token);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new AdbPairResult(false, AdbFailureKind.NetworkUnreachable, "Timed out waiting for the device to respond.");
        }
    }

    /// <summary>Same distinction as <see cref="PairWithTimeoutHandlingAsync"/>, for connect operations.</summary>
    private async Task<AdbConnectResult> ConnectWithTimeoutHandlingAsync(
        string ip, int port, CancellationTokenSource cts, CancellationToken callerToken)
    {
        try
        {
            return await _adbService.AdbConnectAsync(ip, port, cts.Token);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new AdbConnectResult(false, null, AdbFailureKind.NetworkUnreachable, "Timed out waiting for the device to respond.");
        }
    }

    private const string NetworkFailureHint = "Couldn't reach the device. Check Wi-Fi, firewall, or AP/client isolation settings.";

    private static string BuildFailureMessage(AdbFailureKind kind, string? detail) => kind switch
    {
        AdbFailureKind.NetworkUnreachable => detail is null ? NetworkFailureHint : $"{NetworkFailureHint} (adb: {detail})",
        AdbFailureKind.Rejected => detail ?? "The device rejected the request.",
        _ => "Operation failed. Please try again."
    };

    /// <summary>
    /// QR pairing uses an auto-generated password embedded in the scanned code, so a genuine
    /// credential rejection can't happen here — any failure is effectively a connectivity issue,
    /// regardless of how adb happens to word its response.
    /// </summary>
    private static string BuildQrPairingFailureMessage(string? detail) =>
        detail is null ? NetworkFailureHint : $"{NetworkFailureHint} (adb: {detail})";

    #endregion

    #region Helpers

    private ServiceClass ClassifyServiceType(string serviceType)
    {
        if (serviceType == $"{_pairingKey}.{PairingService}") return ServiceClass.QrPairing;
        if (serviceType.EndsWith(ConnectService, StringComparison.Ordinal)) return ServiceClass.Connect;
        if (serviceType.EndsWith(PairingService, StringComparison.Ordinal)) return ServiceClass.ManualPairing;
        return ServiceClass.Unknown;
    }

    private static (string Ip, int Port) ParseIpAndPortFromSerial(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("Serial is empty.", nameof(serial));

        int portSeparatorIndex;
        if (serial.StartsWith('['))
        {
            // IPv6 format: "[::1]:5555"
            int closingBracket = serial.IndexOf(']');
            if (closingBracket < 0 || closingBracket + 1 >= serial.Length || serial[closingBracket + 1] != ':')
                throw new ArgumentException("IPv6 serial is not in the correct format.", nameof(serial));
            portSeparatorIndex = closingBracket + 1;
        }
        else
        {
            // IPv4 format: "192.168.1.1:5555"
            portSeparatorIndex = serial.LastIndexOf(':');
            if (portSeparatorIndex < 0)
                throw new ArgumentException("Serial is not in the correct format.", nameof(serial));
        }

        var ip = serial[..portSeparatorIndex];
        if (!int.TryParse(serial[(portSeparatorIndex + 1)..], out var port))
            throw new ArgumentException("Serial port is not a valid integer.", nameof(serial));

        return (ip, port);
    }

    private async Task<(string Ip, int Port)?> TryResolveMdnsSerialAsync(string serial, CancellationToken cancellationToken)
    {
        string serviceTypeFqdn;

        if (serial.Contains($".{ConnectService.Replace(".local.", string.Empty)}"))
            serviceTypeFqdn = ConnectService;
        else if (serial.Contains($".{PairingService.Replace(".local.", string.Empty)}"))
            serviceTypeFqdn = PairingService;
        else
            return null;

        // The listener must be running to send a query and receive an answer.
        StartListenerIfNeeded();

        var tcs = new TaskCompletionSource<(string Ip, int Port)?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnAnswer(object? sender, MessageEventArgs e)
        {
            var allRecords = e.Message.Answers.Concat(e.Message.AdditionalRecords);

            var srv = allRecords.OfType<SRVRecord>()
                .FirstOrDefault(r => r.Name.ToString().StartsWith(serial, StringComparison.OrdinalIgnoreCase));
            if (srv is null) return;

            var aRecord = allRecords.OfType<ARecord>()
                .FirstOrDefault(r => r.Name == srv.Target);
            if (aRecord is null || aRecord.Address.AddressFamily != AddressFamily.InterNetwork) return;

            tcs.TrySetResult((aRecord.Address.ToString(), srv.Port));
        }

        _multicast!.AnswerReceived += OnAnswer;
        try
        {
            var query = new Message();
            query.Questions.Add(new Question
            {
                Name = new DomainName(serviceTypeFqdn.TrimEnd('.')),
                Type = DnsType.PTR
            });
            _multicast.SendQuery(query);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(AdbOperationTimeoutSeconds));
            await using var _ = linkedCts.Token.Register(() => tcs.TrySetResult(null));

            return await tcs.Task;
        }
        catch { return null; }
        finally
        {
            _multicast!.AnswerReceived -= OnAnswer;
        }
    }

    private enum ServiceClass { Unknown, QrPairing, Connect, ManualPairing }

    #endregion

    public void Dispose()
    {
        _sd?.Dispose();
        _multicast?.Dispose();
    }
}
