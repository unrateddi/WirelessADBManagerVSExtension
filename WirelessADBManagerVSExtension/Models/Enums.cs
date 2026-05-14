namespace WirelessADBManagerVSExtension.Models;

public enum DeviceStates
{
    ManualPair,
    Pairing,
    Connecting,
    Connected,
    Disconnected,
    /// <summary>USB-connected device that can be switched to TCP/IP wireless mode.</summary>
    UsbTcpip
}

public enum ConnectionType
{
    Wireless,
    Usb
}
