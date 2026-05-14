using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.UI;
using QRCoder;
using System.Runtime.Serialization;
using System.Text;
using WirelessADBManagerVSExtension.Models;
using WirelessADBManagerVSExtension.Services;

namespace WirelessADBManagerVSExtension;

/// <summary>
/// ViewModel for the WirelessADBManagerToolWindowContent remote user control.
/// </summary>
[DataContract]
public sealed class WirelessADBManagerToolWindowData : NotifyPropertyChangedObject, IDisposable
{
    private readonly WirelessAdbManagerService _wirelessAdbManagerService;
    private readonly VisualStudioExtensibility _extensibility;

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private ObservableList<DeviceInfo> _devices = [];

    [DataMember]
    public ObservableList<DeviceInfo> Devices
    {
        get => this._devices;
        set => SetProperty(ref this._devices, value);
    }

    private string? _qrDataSvg;

    [DataMember]
    public string? QrDataSvg
    {
        get => this._qrDataSvg;
        set => SetProperty(ref this._qrDataSvg, value);
    }

    internal WirelessADBManagerToolWindowData(WirelessAdbManagerService wirelessAdbManagerService, VisualStudioExtensibility extensibility)
    {
        _wirelessAdbManagerService = wirelessAdbManagerService;
        _extensibility = extensibility;

        DeviceActionCommand = new AsyncCommand((parameter, clientContext, cancellationToken) =>
        {
            if (parameter is DeviceInfo deviceInfo)
                return DecideActionAsync(deviceInfo);

            return Task.CompletedTask;
        });

        QrDataSvg = CreateQrSvgFromData(wirelessAdbManagerService.QrData);
    }

    internal async Task DiscoverDevicesAsync()
    {
        // Start USB device tracking in parallel — push updates whenever a USB device is plugged/unplugged.
        _ = Task.Run(() => TrackUsbDevicesAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

        var deviceInfos = _wirelessAdbManagerService.DiscoverDevicesAsync(_cancellationTokenSource.Token);

        await foreach (var deviceInfo in deviceInfos.WithCancellation(_cancellationTokenSource.Token))
        {
            var cachedDeviceInfo = Devices.FirstOrDefault(d => d.Ip == deviceInfo.Ip);

            if (cachedDeviceInfo is not null)
            {
                cachedDeviceInfo.Model = deviceInfo.Model;
                cachedDeviceInfo.IsConnected = deviceInfo.IsConnected;
                cachedDeviceInfo.IsPaired = deviceInfo.IsPaired;
                cachedDeviceInfo.State = deviceInfo.State;
                // AdbSerial may arrive later (e.g. after auto-connect resolves) — only update
                // when the incoming value is non-null so we never overwrite a known serial.
                if (deviceInfo.AdbSerial is not null)
                    cachedDeviceInfo.AdbSerial = deviceInfo.AdbSerial;
                continue;
            }

            deviceInfo.ActionCommand = DeviceActionCommand;

            Devices.Add(deviceInfo);
        }
    }

    private async Task TrackUsbDevicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var snapshot in _wirelessAdbManagerService.TrackUsbDevicesAsync(cancellationToken))
            {
                // Remove USB entries that are no longer present (unplugged).
                // Don't touch wireless entries — they have no UsbSerial when in wireless state.
                var toRemove = Devices
                    .Where(d => d.State == DeviceStates.UsbTcpip && !snapshot.Any(s => s.UsbSerial == d.UsbSerial))
                    .ToList();
                foreach (var item in toRemove)
                    Devices.Remove(item);

                foreach (var device in snapshot)
                {
                    var existing = Devices.FirstOrDefault(d => d.UsbSerial == device.UsbSerial);
                    if (existing is not null)
                    {
                        existing.Model = device.Model;
                        existing.State = device.State;
                        existing.IsWirelessAdbEnabled = device.IsWirelessAdbEnabled;
                    }
                    else
                    {
                        device.ActionCommand = DeviceActionCommand;
                        Devices.Add(device);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task SwitchToWirelessAsync(DeviceInfo deviceInfo)
    {
        // Read port from settings via service.
        var port = await _wirelessAdbManagerService.GetTcpIpPortAsync(_cancellationTokenSource.Token);

        // Auto-detect WiFi IP; prompt user as fallback.
        var deviceIp = await _wirelessAdbManagerService.TryGetWifiIpForUsbDeviceAsync(
            deviceInfo.UsbSerial!, _cancellationTokenSource.Token);

        deviceIp ??= await _extensibility.Shell().ShowPromptAsync(
                "Could not auto-detect the device\u2019s WiFi IP address. Enter it manually:",
                new Microsoft.VisualStudio.Extensibility.Shell.InputPromptOptions
                {
                    Title = "WiFi IP Address",
                    DefaultText = "192.168.1."
                },
                _cancellationTokenSource.Token);

        if (string.IsNullOrWhiteSpace(deviceIp))
            return;

        var result = await _wirelessAdbManagerService.EnableTcpIpModeAsync(
            deviceInfo, deviceIp, port, _cancellationTokenSource.Token);

        if (result is null)
            return; // silent failure — ADB errors are logged to debug output

        // Update the existing entry in-place — it's already in Devices as a USB entry.
        // Update all fields so the row transitions to wireless appearance without being removed/re-added.
        var existing = Devices.FirstOrDefault(d => d.UsbSerial == deviceInfo.UsbSerial)
                    ?? Devices.FirstOrDefault(d => d.Ip == result.Ip);

        if (existing is not null)
        {
            existing.Ip = result.Ip;
            existing.Model = result.Model;
            existing.AdbSerial = result.AdbSerial;
            existing.IsConnected = result.IsConnected;
            existing.IsPaired = result.IsPaired;
            existing.IsWirelessAdbEnabled = result.IsWirelessAdbEnabled;
            existing.State = result.State;
            // Clear UsbSerial so the device is no longer treated as USB by the tracker.
            existing.UsbSerial = null;
        }
        else
        {
            result.ActionCommand = DeviceActionCommand;
            Devices.Add(result);
        }
    }

    private async Task ConnectDeviceAsync(DeviceInfo deviceInfo)
    {
        var connectedDeviceInfo = await _wirelessAdbManagerService.ConnectDeviceAsync(deviceInfo, _cancellationTokenSource.Token);

        deviceInfo.Model = connectedDeviceInfo.Model;
        deviceInfo.IsConnected = connectedDeviceInfo.IsConnected;
        deviceInfo.IsPaired = connectedDeviceInfo.IsPaired;
        deviceInfo.State = connectedDeviceInfo.State;
    }

    private async Task PairDeviceManuallyAsync(DeviceInfo deviceInfo)
    {
        var pairingCode = await _extensibility.Shell().ShowPromptAsync("Enter the pairing code displayed on your device", new Microsoft.VisualStudio.Extensibility.Shell.InputPromptOptions()
        {
            Title = "Pair Device with Pairing Code",
            DefaultText = "00000"
        }, _cancellationTokenSource.Token);

        if (string.IsNullOrWhiteSpace(pairingCode))
            return;

        var connectedDeviceInfo = await _wirelessAdbManagerService.ManualPairDeviceAsync(deviceInfo, pairingCode, _cancellationTokenSource.Token);

        deviceInfo.Model = connectedDeviceInfo.Model;
        deviceInfo.IsConnected = connectedDeviceInfo.IsConnected;
        deviceInfo.IsPaired = connectedDeviceInfo.IsPaired;
        deviceInfo.State = connectedDeviceInfo.State;
    }

    private async Task DisconnectDeviceAsync(DeviceInfo deviceInfo)
    {
        if (deviceInfo.IsWirelessAdbEnabled && !string.IsNullOrEmpty(deviceInfo.AdbSerial))
        {
            var options = new Microsoft.VisualStudio.Extensibility.Shell.PromptOptions<bool>
            {
                Title = "Turn Off Wireless ADB?"
            };
            options.Choices.Add(
                new Microsoft.VisualStudio.Extensibility.Shell.ChoiceDescription("Yes"),
                true);
            options.Choices.Add(
                new Microsoft.VisualStudio.Extensibility.Shell.ChoiceDescription("No"),
                false);

            var turnOff = await _extensibility.Shell().ShowPromptAsync(
                "Would you like to turn off wireless ADB on the device? This can save battery when you no longer need wireless debugging.",
                options,
                _cancellationTokenSource.Token);

            if (turnOff)
                await _wirelessAdbManagerService.DisableWirelessAdbAsync(deviceInfo.AdbSerial, _cancellationTokenSource.Token);
        }

        var disconnectedDeviceInfo = await _wirelessAdbManagerService.DisconnectDeviceAsync(deviceInfo, _cancellationTokenSource.Token);

        deviceInfo.Model = disconnectedDeviceInfo.Model;
        deviceInfo.IsConnected = disconnectedDeviceInfo.IsConnected;
        deviceInfo.IsPaired = disconnectedDeviceInfo.IsPaired;
        deviceInfo.IsWirelessAdbEnabled = false;
        deviceInfo.State = disconnectedDeviceInfo.State;
    }

    private async Task TurnOffWirelessAdbAsync(DeviceInfo deviceInfo)
    {
        // For a USB row the transport serial is UsbSerial; pass it directly.
        var serial = deviceInfo.UsbSerial;
        if (string.IsNullOrEmpty(serial)) return;

        var success = await _wirelessAdbManagerService.DisableWirelessAdbAsync(serial, _cancellationTokenSource.Token);
        if (success)
            deviceInfo.IsWirelessAdbEnabled = false;
    }

    [DataMember]
    public AsyncCommand DeviceActionCommand;

    public async Task DecideActionAsync(DeviceInfo deviceInfo)
    {
        switch (deviceInfo.State)
        {
            case DeviceStates.ManualPair:
                await PairDeviceManuallyAsync(deviceInfo);
                break;
            case DeviceStates.Connected:
                await DisconnectDeviceAsync(deviceInfo);
                break;
            case DeviceStates.Disconnected:
                await ConnectDeviceAsync(deviceInfo);
                break;
            case DeviceStates.UsbTcpip:
                {
                    if (deviceInfo.IsWirelessAdbEnabled)
                    {
                        await TurnOffWirelessAdbAsync(deviceInfo);
                        return;
                    }

                    await SwitchToWirelessAsync(deviceInfo);
                }
                break;
            case DeviceStates.Connecting:
            case DeviceStates.Pairing:
            default:
                break;
        }
    }

    public void CancelAll()
    {
        _cancellationTokenSource.Cancel();
    }

    private static string CreateQrSvgFromData(string qrData)
    {
        using QRCodeGenerator qrGenerator = new();
        using QRCodeData qrCodeData = qrGenerator.CreateQrCode(qrData, QRCodeGenerator.ECCLevel.Q);
        using SvgQRCode svgQrCode = new(qrCodeData);

        var builder = new StringBuilder();
        int size = qrCodeData.ModuleMatrix.Count;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // If the module (square) is black, draw a tiny square in the path
                if (qrCodeData.ModuleMatrix[y][x])
                {
                    // M = MoveTo, H = HorizontalLine, V = VerticalLine, Z = ClosePath
                    builder.Append($"M{x},{y} H{x + 1} V{y + 1} H{x} Z ");
                }
            }
        }

        return builder.ToString();
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }
}
