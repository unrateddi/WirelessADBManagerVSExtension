#pragma warning disable VSEXTPREVIEW_SETTINGS // The settings API is currently in preview and marked as experimental

using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using Microsoft.VisualStudio.Extensibility;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using WirelessADBManagerVSExtension.Settings;

namespace WirelessADBManagerVSExtension.Services;

public partial class AdbService(IAdbClient _adbClient, WirelessADBManagerCategoryObserver _settingsObserver, VisualStudioExtensibility _extensibility)
{
    const string DefaultAdbPath = "C:\\Program Files (x86)\\Android\\android-sdk\\platform-tools\\adb.exe";
    const string AdbPathLocator = "android-sdk\\platform-tools";
    const string AdbExecutableName = "adb.exe";

    readonly IAdbClient _adbClient = _adbClient;
    readonly WirelessADBManagerCategoryObserver _settingsObserver = _settingsObserver;
    readonly VisualStudioExtensibility _extensibility = _extensibility;

    string? _adbPath;

    internal static bool IsAdbServerRunning => AdbServer.Instance.GetStatus().IsRunning;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var adbPathFromSettings = await GetAdbPathFromSettingsAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(adbPathFromSettings) && File.Exists(adbPathFromSettings))
        {
            _adbPath = adbPathFromSettings;
            return;
        }

        var adbPathFromEnvironment = TryToFindAdbPathInPathEnvironmentVariable();
        if (!string.IsNullOrWhiteSpace(adbPathFromEnvironment) && File.Exists(adbPathFromEnvironment))
        {
            _adbPath = adbPathFromEnvironment;
            await SaveAdbPathToSettingsAsync(adbPathFromEnvironment, cancellationToken);
            return;
        }

        if (File.Exists(DefaultAdbPath))
        {
            _adbPath = DefaultAdbPath;
            await SaveAdbPathToSettingsAsync(DefaultAdbPath, cancellationToken);
            return;
        }

        Console.WriteLine("ADB executable not found. Please set the correct path in the extension settings.");

        var options = new Microsoft.VisualStudio.Extensibility.Shell.PromptOptions<bool>()
        {
            Title = "ADB Path Not Found"
        };
        options.Choices.Add(new Microsoft.VisualStudio.Extensibility.Shell.ChoiceDescription("OK"), true);

        await _extensibility.Shell().ShowPromptAsync("ADB path is not set. Please configure it in the extension settings.", options, cancellationToken);
    }

    internal async Task<(bool Success, DeviceData? ConnectedDevice)> AdbConnectAsync(string ip, int port, CancellationToken cancellationToken)
    {
        await EnsureAdbServerIsRunningAsync(cancellationToken);

        try
        {
            await _adbClient.ConnectAsync(ip, port, cancellationToken);
            var devices = await _adbClient.GetDevicesAsync(cancellationToken);
            var device = devices.FirstOrDefault(d => d.Serial == $"{ip}:{port}");

            if (device == default)
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

    internal async Task<bool> AdbDisconnectAsync(string ip, int port, CancellationToken cancellationToken = default)
    {
        await EnsureAdbServerIsRunningAsync(cancellationToken);

        try
        {
            var disconnectResult = await _adbClient.DisconnectAsync(ip, port, cancellationToken);

            return !disconnectResult.StartsWith("error");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error disconnecting ADB: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Disconnects a device using its exact ADB serial string. Works with both
    /// "IP:Port" serials and mDNS instance names (e.g. "adb-XYZ._adb-tls-connect._tcp.").
    /// </summary>
    internal async Task<bool> AdbDisconnectBySerialAsync(string serial, CancellationToken cancellationToken = default)
    {
        await EnsureAdbServerIsRunningAsync(cancellationToken);

        try
        {
            // "host:disconnect:<serial>" — the ADB server accepts the full serial (IP:Port
            // or mDNS name) directly. We send it as a raw host command to avoid the
            // library splitting host and port, which would corrupt mDNS serials.
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, AdbClient.AdbServerPort, cancellationToken);
            var stream = tcp.GetStream();
            await SendCommandOnStreamAsync(stream, $"host:disconnect:{serial}", cancellationToken);

            // Read the length-prefixed response string.
            var lenBuf = new byte[4];
            await stream.ReadExactlyAsync(lenBuf, cancellationToken);
            var len = int.Parse(Encoding.UTF8.GetString(lenBuf), System.Globalization.NumberStyles.HexNumber);
            var bodyBuf = new byte[len];
            if (len > 0) await stream.ReadExactlyAsync(bodyBuf, cancellationToken);
            var response = Encoding.UTF8.GetString(bodyBuf);

            return !response.StartsWith("error", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error disconnecting ADB by serial: {ex.Message}");
            return false;
        }
    }

    internal async Task<bool> AdbPairAsync(string ip, int port, string password, CancellationToken cancellationToken)
    {
        await EnsureAdbServerIsRunningAsync(cancellationToken);

        try
        {
            var pairingResult = await _adbClient.PairAsync(ip, port, password, cancellationToken);

            return !pairingResult.StartsWith("Failed");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the configured TCP/IP port for wireless ADB. If <c>TcpIpUseRandomPort</c> is
    /// enabled, returns a random ephemeral port (49152–65535) instead.
    /// </summary>
    internal async Task<int> GetTcpIpPortAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _settingsObserver.GetSnapshotAsync(cancellationToken);
            if (snapshot.TcpIpUseRandomPort.ValueOrDefault(false))
                return System.Random.Shared.Next(49152, 65536);
            return snapshot.TcpIpPort.ValueOrDefault(5555);
        }
        catch
        {
            return 5555;
        }
    }

    internal async Task<IEnumerable<DeviceData>> AdbListDevicesAsync(CancellationToken cancellationToken)
    {
        await EnsureAdbServerIsRunningAsync(cancellationToken);

        try
        {
            return await _adbClient.GetDevicesAsync(cancellationToken);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Streams device list snapshots via ADB's <c>host:track-devices</c> command. Each yielded
    /// list reflects the full current device state. Runs until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    internal async IAsyncEnumerable<IReadOnlyList<DeviceData>> TrackDevicesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await EnsureAdbServerIsRunningAsync(cancellationToken);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, AdbClient.AdbServerPort, cancellationToken);
        var stream = tcp.GetStream();

        // Send "host:track-devices" — the server keeps the connection open and pushes updates.
        var cmd = "host:track-devices";
        var payload = Encoding.UTF8.GetBytes(cmd);
        var header = Encoding.UTF8.GetBytes($"{payload.Length:X4}");
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        // Read initial OKAY status
        var statusBuf = new byte[4];
        await stream.ReadExactlyAsync(statusBuf, cancellationToken);
        if (Encoding.UTF8.GetString(statusBuf) != "OKAY")
            yield break;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Each chunk: 4-char hex length + "serial\tstate\n" lines
            var lenBuf = new byte[4];
            await stream.ReadExactlyAsync(lenBuf, cancellationToken);
            var bodyLen = int.Parse(Encoding.UTF8.GetString(lenBuf), System.Globalization.NumberStyles.HexNumber);
            var bodyBuf = new byte[bodyLen];
            if (bodyLen > 0)
                await stream.ReadExactlyAsync(bodyBuf, cancellationToken);

            var lines = Encoding.UTF8.GetString(bodyBuf)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);

            var devices = new List<DeviceData>();
            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts.Length < 2) continue;
                var serial = parts[0].Trim();
                var stateStr = parts[1].Trim();
                devices.Add(new DeviceData { Serial = serial, State = stateStr == "device" ? DeviceState.Online : DeviceState.Offline });
            }

            yield return devices;
        }
    }

    /// <summary>
    /// Reads ro.product.model via ADB shell for a USB-connected device.
    /// Returns null on failure.
    /// </summary>
    internal async Task<string?> GetModelAsync(string usbSerial, CancellationToken cancellationToken)
    {
        try
        {
            var device = new DeviceData { Serial = usbSerial };
            var receiver = new ConsoleOutputReceiver();
            await _adbClient.ExecuteRemoteCommandAsync("getprop ro.product.model", device, receiver, cancellationToken);
            var model = receiver.ToString().Trim();
            return string.IsNullOrEmpty(model) ? null : model;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetModelAsync failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Attempts to get the WiFi (wlan0) IPv4 address from a USB-connected device via ADB shell.
    /// Returns null if the address cannot be determined.
    /// </summary>
    internal async Task<string?> GetWifiIpAsync(string usbSerial, CancellationToken cancellationToken)
    {
        await EnsureAdbServerIsRunningAsync(cancellationToken);

        try
        {
            // Try wlan0 first, then a broader inet scan
            var device = new DeviceData { Serial = usbSerial };
            var receiver = new ConsoleOutputReceiver();
            await _adbClient.ExecuteRemoteCommandAsync("ip -f inet addr show wlan0", device, receiver, cancellationToken);
            var output = receiver.ToString();

            // Parse "inet 192.168.x.x/24"
            var match = IpAddressRegex().Match(output);
            if (match.Success)
                return match.Groups[1].Value;

            // Fallback: getprop (older Android)
            var receiver2 = new ConsoleOutputReceiver();
            await _adbClient.ExecuteRemoteCommandAsync("getprop dhcp.wlan0.ipaddress", device, receiver2, cancellationToken);
            var prop = receiver2.ToString().Trim();
            if (!string.IsNullOrEmpty(prop) && IPAddress.TryParse(prop, out _))
                return prop;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetWifiIpAsync failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Reads the active wireless ADB TCP port from the device via ADB shell.
    /// Returns the port number if wireless ADB is on, or -1 if it is off or unreachable.
    /// </summary>
    internal async Task<int> GetWirelessAdbPortAsync(string usbSerial, CancellationToken cancellationToken)
    {
        try
        {
            var device = new DeviceData { Serial = usbSerial };
            var receiver = new ConsoleOutputReceiver();
            await _adbClient.ExecuteRemoteCommandAsync("getprop service.adb.tcp.port", device, receiver, cancellationToken);
            var portStr = receiver.ToString().Trim();
            return int.TryParse(portStr, out var port) && port > 0 ? port : -1;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetWirelessAdbPortAsync failed: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Switches the device's adbd back to USB-only mode, turning off wireless ADB TCP/IP
    /// listening. Equivalent to "adb usb". Works over both USB and TCP transports.
    /// </summary>
    internal async Task<bool> DisableWirelessAdbAsync(string serial, CancellationToken cancellationToken)
    {
        await EnsureAdbServerIsRunningAsync(cancellationToken);
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, AdbClient.AdbServerPort, cancellationToken);
            var stream = tcp.GetStream();
            await SendCommandOnStreamAsync(stream, $"host:transport:{serial}", cancellationToken);
            await SendCommandOnStreamAsync(stream, "usb:", cancellationToken);
            // Consume the response text (e.g. "restarting in USB mode"); ignore failures here.
            using var reader = new StreamReader(stream, Encoding.UTF8);
            await reader.ReadToEndAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DisableWirelessAdbAsync failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Switches a USB-connected device to TCP/IP mode on the given port, then connects to it
    /// wirelessly. Returns the connected <see cref="DeviceData"/> on success, null on failure.
    /// </summary>
    internal async Task<(bool Success, DeviceData? ConnectedDevice)> EnableTcpIpModeAsync(
        string usbSerial, string wifiIp, int port, CancellationToken cancellationToken)
    {
        await EnsureAdbServerIsRunningAsync(cancellationToken);

        try
        {
            // Two-step ADB wire protocol: select device transport, then send tcpip service.
            // Does NOT require root on the device.
            await SendAdbTcpIpAsync(usbSerial, port, cancellationToken);

            // Small delay — the device ADB daemon takes ~1s to restart.
            await Task.Delay(1500, cancellationToken);

            return await AdbConnectAsync(wifiIp, port, cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EnableTcpIpModeAsync failed: {ex.Message}");
            return (false, default);
        }
    }

    private async Task EnsureAdbServerIsRunningAsync(CancellationToken cancellationToken)
    {
        if (!AdbServer.Instance.GetStatus().IsRunning && !string.IsNullOrEmpty(_adbPath))
        {
            AdbServer server = new();
            StartServerResult result = await server.StartServerAsync(_adbPath, false, cancellationToken);
            if (result != StartServerResult.Started)
            {
                Console.WriteLine("Can't start adb server");
            }
        }
    }

    #region HELPERS

    private async Task<string?> GetAdbPathFromSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _settingsObserver.GetSnapshotAsync(cancellationToken);

            return snapshot.ADBPath.ValueOrDefault(string.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting ADB path from settings: {ex.Message}");
        }

        return null; // Return null if not found or error
    }

    private async Task SaveAdbPathToSettingsAsync(string adbPath, CancellationToken cancellationToken)
    {
        try
        {
            var writeResult = await _extensibility.Settings().WriteAsync(
                batch =>
                {
                    batch.WriteSetting(SettingDefinitions.ADBPath, value: adbPath);
                },
                description: "Updating the settings' value",
                cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving ADB path to settings: {ex.Message}");
        }
    }

    private static string? TryToFindAdbPathInPathEnvironmentVariable()
    {
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
        var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);

        var allPathsString = string.IsNullOrWhiteSpace(userPath)
            ? machinePath
            : string.IsNullOrWhiteSpace(machinePath)
                ? userPath
                : userPath + ";" + machinePath;

        if (string.IsNullOrWhiteSpace(allPathsString))
            return null;

        var paths = allPathsString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var adbPath = paths.FirstOrDefault(p => p.Contains(AdbPathLocator, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(adbPath))
            return null;

        var fullPath = Path.Combine(adbPath, AdbExecutableName);
        return File.Exists(fullPath) ? fullPath : null;
    }

    [GeneratedRegex(@"inet (\d+\.\d+\.\d+\.\d+)/")]
    private static partial Regex IpAddressRegex();

    /// <summary>
    /// Switches a USB-connected device to TCP/IP mode using the two-step ADB wire protocol:
    /// 1. "host:transport:&lt;serial&gt;" — select the device transport on the socket
    /// 2. "tcpip:&lt;port&gt;" — send the tcpip service to the device daemon
    /// This is the exact protocol "adb -s &lt;serial&gt; tcpip &lt;port&gt;" uses and requires no root.
    /// </summary>
    private static async Task SendAdbTcpIpAsync(string serial, int port, CancellationToken cancellationToken)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, AdbClient.AdbServerPort, cancellationToken);
        var stream = tcp.GetStream();

        await SendCommandOnStreamAsync(stream, $"host:transport:{serial}", cancellationToken);
        await SendCommandOnStreamAsync(stream, $"tcpip:{port}", cancellationToken);

        // Read and discard the plain-text response ("restarting in TCP mode port: X\n").
        using var reader = new StreamReader(stream, Encoding.UTF8);
        await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>Sends one ADB wire-protocol command on an open stream and reads the OKAY/FAIL status.</summary>
    private static async Task SendCommandOnStreamAsync(NetworkStream stream, string command, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(command);
        var header = Encoding.UTF8.GetBytes($"{payload.Length:X4}");
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var statusBuf = new byte[4];
        await stream.ReadExactlyAsync(statusBuf, cancellationToken);
        if (Encoding.UTF8.GetString(statusBuf) != "OKAY")
        {
            var errLenBuf = new byte[4];
            await stream.ReadExactlyAsync(errLenBuf, cancellationToken);
            var errLen = int.Parse(Encoding.UTF8.GetString(errLenBuf), System.Globalization.NumberStyles.HexNumber);
            var errBuf = new byte[errLen];
            await stream.ReadExactlyAsync(errBuf, cancellationToken);
            throw new InvalidOperationException(Encoding.UTF8.GetString(errBuf));
        }
    }

    #endregion

}
