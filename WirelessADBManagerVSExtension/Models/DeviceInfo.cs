using Microsoft.VisualStudio.Extensibility.UI;
using System.Runtime.Serialization;

namespace WirelessADBManagerVSExtension.Models;

file static class DeviceIconUris
{
    // AppContext.BaseDirectory points to the service host, not the extension DLL.
    // Assembly.Location is guaranteed to be the extension's output directory.
    private static readonly string _resourcesDir =
        Path.Combine(Path.GetDirectoryName(typeof(DeviceInfo).Assembly.Location)!, "Resources");

    public static readonly string Wireless =
        new Uri(Path.Combine(_resourcesDir, "WirelessADBManager_Device_Icon.png")).AbsoluteUri;

    public static readonly string Usb =
        new Uri(Path.Combine(_resourcesDir, "WirelessADBManager_USB_Device_Icon.png")).AbsoluteUri;
}

[DataContract]
public class DeviceInfo : NotifyPropertyChangedObject
{
    private string _model;

    [DataMember]
    public string Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
    }

    private string _ip;

    [DataMember]
    public string Ip
    {
        get => _ip;
        set => SetProperty(ref _ip, value);
    }

    private string? _adbSerial;

    /// <summary>
    /// The exact serial ADB uses for this device. Used by the service for connect/disconnect
    /// operations. Not data-bound to the remote UI.
    /// </summary>
    public string? AdbSerial
    {
        get => _adbSerial;
        set => SetProperty(ref _adbSerial, value);
    }

    private string? _usbSerial;

    /// <summary>The raw USB serial (e.g. "A54J023318002898"). Populated only for USB-connected devices.</summary>
    [DataMember]
    public string? UsbSerial
    {
        get => _usbSerial;
        set => SetProperty(ref _usbSerial, value);
    }
    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    private bool _isPaired;
    public bool IsPaired
    {
        get => _isPaired;
        set => SetProperty(ref _isPaired, value);
    }

    private DeviceStates _state;
    public DeviceStates State
    {
        get => _state;
        set
        {
            SetProperty(ref _state, value);

            StateText = value switch
            {
                DeviceStates.ManualPair => "Pair",
                DeviceStates.Pairing => "Pairing",
                DeviceStates.Connecting => "Connecting",
                DeviceStates.Connected => "Disconnect",
                DeviceStates.Disconnected => "Connect",
                DeviceStates.UsbTcpip => _isWirelessAdbEnabled ? "Turn off wireless ADB" : "Switch to Wireless",
                DeviceStates.ConnectionFailed => "Connect",
                DeviceStates.NotPaired => "Not Paired",
                _ => "Unknown"
            };

            // No pairing offer is currently available for this device — the action button
            // would either do nothing useful or fail, so disable it until pairing is offered.
            IsActionEnabled = value != DeviceStates.NotPaired;

            DeviceIconUri = value == DeviceStates.UsbTcpip
                ? DeviceIconUris.Usb
                : DeviceIconUris.Wireless;
        }
    }

    private string _stateText;

    [DataMember]
    public string StateText
    {
        get => _stateText;
        set => SetProperty(ref _stateText, value);
    }

    private bool _isActionEnabled = true;

    /// <summary>False when the action button has nothing valid to do (e.g. no pairing offer available yet).</summary>
    [DataMember]
    public bool IsActionEnabled
    {
        get => _isActionEnabled;
        set => SetProperty(ref _isActionEnabled, value);
    }

    private string _deviceIconUri = DeviceIconUris.Wireless;

    [DataMember]
    public string DeviceIconUri
    {
        get => _deviceIconUri;
        set => SetProperty(ref _deviceIconUri, value);
    }

    [DataMember]
    public AsyncCommand ActionCommand { get; set; }

    private bool _isWirelessAdbEnabled;

    /// <summary>True when wireless ADB (TCP/IP) mode is currently active on this device.</summary>
    [DataMember]
    public bool IsWirelessAdbEnabled
    {
        get => _isWirelessAdbEnabled;
        set => SetProperty(ref _isWirelessAdbEnabled, value);
    }

    private string? _failureReason;

    /// <summary>Populated when pairing or connecting fails so the tool window can show a diagnostic dialog.</summary>
    [DataMember]
    public string? FailureReason
    {
        get => _failureReason;
        set => SetProperty(ref _failureReason, value);
    }
}