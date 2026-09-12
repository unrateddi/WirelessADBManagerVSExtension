namespace WirelessADBManagerVSExtension.Models;

public enum DeviceStates
{
    ManualPair,
    Pairing,
    Connecting,
    Connected,
    Disconnected,
    /// <summary>USB-connected device that can be switched to TCP/IP wireless mode.</summary>
    UsbTcpip,
    /// <summary>Device paired successfully but the subsequent ADB connect failed.</summary>
    ConnectionFailed,
    /// <summary>Device is visible on the network but has never been paired and isn't currently offering a pairing code — no action is possible until the phone opens the pairing screen.</summary>
    NotPaired
}

public enum ConnectionType
{
    Wireless,
    Usb
}
