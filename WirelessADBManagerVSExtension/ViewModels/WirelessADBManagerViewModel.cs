using Microsoft.VisualStudio.PlatformUI;
using QRCoder;
using QRCoder.Xaml;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using WirelessADBManagerVSExtension.Helpers;
using WirelessADBManagerVSExtension.Models;
using WirelessADBManagerVSExtension.Services;

namespace WirelessADBManagerVSExtension.ViewModels;

public class WirelessAdbManagerViewModel : BaseNotify
{
    private readonly WirelessAdbManagerService _wirelessAdbManagerService = new();

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private ObservableCollection<DeviceInfo> _devices = [];
    public ObservableCollection<DeviceInfo> Devices
    {
        get => _devices;
        set
        {
            _devices = value;
            OnPropertyChanged();
        }
    }

    private DrawingImage _qrDataImageSource;
    public DrawingImage QrDataImageSource
    {
        get => _qrDataImageSource;
        set
        {
            _qrDataImageSource = value;
            OnPropertyChanged();
        }
    }

    private SolidColorBrush _backgroundBrush = new(ColorHelpers.GetVsThemeColor(EnvironmentColors.ToolWindowBackgroundColorKey, Colors.White));
    public SolidColorBrush BackgroundBrush
    {
        get => _backgroundBrush;
        set
        {
            _backgroundBrush = value;
            OnPropertyChanged();
        }
    }

    private SolidColorBrush _textBrush = new(ColorHelpers.GetVsThemeColor(EnvironmentColors.ToolWindowTextBrushKey, Colors.White));
    public SolidColorBrush TextBrush
    {
        get => _textBrush;
        set
        {
            _textBrush = value;
            OnPropertyChanged();
        }
    }

    public WirelessAdbManagerViewModel()
    {
        QrDataImageSource = CreateQrImageSourceFromData(_wirelessAdbManagerService.GetQrData());

        Task.Run(DiscoverDevicesAsync);
    }

    public async Task DiscoverDevicesAsync()
    {
        var deviceInfos = _wirelessAdbManagerService.DiscoverDevicesAsync(_cancellationTokenSource.Token);

        await foreach (var deviceInfo in deviceInfos.WithCancellation(_cancellationTokenSource.Token))
        {
            var cachedDeviceInfo = Devices.FirstOrDefault(d => d.Ip == deviceInfo.Ip);

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (cachedDeviceInfo is not null)
                {
                    cachedDeviceInfo.Model = deviceInfo.Model;
                    cachedDeviceInfo.IsConnected = deviceInfo.IsConnected;
                    cachedDeviceInfo.IsPaired = deviceInfo.IsPaired;
                    cachedDeviceInfo.State = deviceInfo.State;
                    return;
                }

                Devices.Add(deviceInfo);
            });
        }
    }

    private async Task ConnectDeviceAsync(DeviceInfo deviceInfo)
    {
        var connectedDeviceInfo = await _wirelessAdbManagerService.ConnectDeviceAsync(deviceInfo, _cancellationTokenSource.Token);

        Application.Current.Dispatcher.Invoke(() =>
        {
            deviceInfo.Model = connectedDeviceInfo.Model;
            deviceInfo.IsConnected = connectedDeviceInfo.IsConnected;
            deviceInfo.IsPaired = connectedDeviceInfo.IsPaired;
            deviceInfo.State = connectedDeviceInfo.State;
        });
    }

    private async Task PairDeviceManuallyAsync(DeviceInfo deviceInfo)
    {
        string password = string.Empty;

        var connectedDeviceInfo = await _wirelessAdbManagerService.ManualPairDeviceAsync(deviceInfo, password, _cancellationTokenSource.Token);

        Application.Current.Dispatcher.Invoke(() =>
        {
            deviceInfo.Model = connectedDeviceInfo.Model;
            deviceInfo.IsConnected = connectedDeviceInfo.IsConnected;
            deviceInfo.IsPaired = connectedDeviceInfo.IsPaired;
            deviceInfo.State = connectedDeviceInfo.State;
        });
    }

    private async Task DisconnectDeviceAsync(DeviceInfo deviceInfo)
    {
        var disconnectedDeviceInfo = await _wirelessAdbManagerService.DisconnectDeviceAsync(deviceInfo, _cancellationTokenSource.Token);

        Application.Current.Dispatcher.Invoke(() =>
        {
            deviceInfo.Model = disconnectedDeviceInfo.Model;
            deviceInfo.IsConnected = disconnectedDeviceInfo.IsConnected;
            deviceInfo.IsPaired = disconnectedDeviceInfo.IsPaired;
            deviceInfo.State = disconnectedDeviceInfo.State;
        });
    }

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

    private DrawingImage CreateQrImageSourceFromData(string qrData)
    {
        QRCodeGenerator qrGenerator = new();
        QRCodeData qrCodeData = qrGenerator.CreateQrCode(qrData, QRCodeGenerator.ECCLevel.Q);
        XamlQRCode qrCode = new(qrCodeData);
        return qrCode.GetGraphic(new System.Windows.Size(10, 10), BackgroundBrush, TextBrush, true);
    }
}