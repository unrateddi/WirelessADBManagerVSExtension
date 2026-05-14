# Wireless ADB Manager for Visual Studio

Wireless ADB Manager is a Visual Studio extension that helps you pair, connect and manage Android devices for Wireless Debugging — all from within Visual Studio, without leaving your IDE.

> **v2.0** is a full rewrite built on the new [VisualStudio.Extensibility](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility) out-of-process model, targeting Visual Studio 2022 17.14 and later. It brings USB-to-wireless switching, automatic device model detection, wireless ADB state management, and a unified device list alongside all the original wireless pairing and connection features.

> ⚠️ **Upgrading from v1?** Due to the architectural change to the new VS Extensibility model, v2 is registered as a separate extension in Visual Studio. After updating, you may see two entries for Wireless ADB Manager under **Extensions → Manage Extensions**. You can safely uninstall the older v1 entry — it will no longer receive updates.

## What you can do

- Easily manage all your ADB connections — both wireless and USB — from a single window inside Visual Studio

- Pair and connect Android devices by scanning a QR code for Wireless Debugging

- Pair and connect Android devices by entering a pairing code

- Directly connect to already-paired devices without scanning a QR code or entering a pairing code again

- Disconnect a connected device

- **Switch a USB-connected device to wireless ADB in one click** — the extension detects the device's WiFi IP automatically and handles the `adb tcpip` switch for you, no terminal needed

- **See the model name** of USB-connected devices directly in the list

- **Know at a glance whether wireless ADB is already on** for any USB-connected device

- **Turn off wireless ADB** from the USB device row when you no longer need it, or be prompted automatically when disconnecting a device you switched to wireless — saving battery on the device

## Get it here!

✅ Visual Studio Marketplace: https://marketplace.visualstudio.com/items?itemName=dimitrios-iliopoulos.WirelessADBManager

✅ Search it in the Visual Studio Extension Manager

## Getting Started

Usually no setup is required after installing the extension. Wireless ADB Manager will use a running ADB server instance without an issue. In case the ADB server is down or killed, Wireless ADB Manager will try to start it. To do so it needs to know the location of `adb.exe`. By default it will try to find it in the PATH environment variables (both User and Machine) by identifying a path containing `android-sdk\platform-tools`. As a fallback it will try the default VS installation path `C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe`. If neither is found, the extension will prompt you to set the correct path in its settings. You can also set it manually at any time via the extension settings under **Tools → Options**.

## Usage

The Wireless ADB Manager can be found under the **Tools** menu in Visual Studio. The PC and Android devices must be on the same network for wireless features to work.

#### Switch a USB-connected device to wireless ADB

If your Android device is connected via USB with **Developer Options** and **USB Debugging** enabled, it will appear automatically in the device list with a USB icon and a **"Switch to Wireless"** button. The extension will:

1. Detect the device's WiFi IP address automatically (or prompt you to enter it if it cannot be determined)
2. Enable TCP/IP mode on the device via `adb tcpip`
3. Connect to the device wirelessly

Once switched, the row transitions to a wireless entry with the device ready to use. If wireless ADB was already enabled on the device before you plugged in the USB cable, a **"Turn off wireless ADB"** button is shown next to the USB row so you can disable it without a terminal.

#### Turn off wireless ADB

There are two ways to turn off wireless ADB on a device (switches adbd back to USB-only mode, equivalent to `adb usb`):

- **From the USB device row** — if the device has wireless ADB on while the USB cable is connected, a "Turn off wireless ADB" button appears directly in the list
- **On disconnect** — when you disconnect a device that was switched to wireless by this extension, a prompt asks *"Would you like to turn off wireless ADB on the device?"* so you can disable it in one step to save battery

#### Pair and connect with QR code

The extension automatically displays a QR code ready to scan. On the Android device, go to **Developer Settings → Wireless Debugging** and tap **"Pair device with QR code"**. Scan the QR code shown by the extension. After pairing completes the device will connect automatically and appear as a target in Visual Studio.

#### Pair and connect with pairing code

In **Wireless Debugging** on the Android device tap **"Pair device with pairing code"**. This shows the device's IP, port and a 6-digit pairing code, and announces the device on the network. The device will appear in the extension's list with a **"Pair"** button. Click it, enter the pairing code shown on the device, and the extension will pair and connect.

#### Connect to an already-paired device

Devices that have previously been paired show a **"Connect"** button when they are discovered on the network (i.e. Wireless Debugging is enabled on the device). No additional action on the device is needed. If the pairing is still valid the connection will succeed immediately.

#### Disconnect a device

A connected device shows a **"Disconnect"** button. Clicking it disconnects the ADB session. Pairing is preserved, so you can reconnect instantly without going through pairing again. If the device was switched to wireless by this extension, you will be asked whether to turn off wireless ADB on the device as well.

## How it works

The extension is built on the **VisualStudio.Extensibility out-of-process model** — it runs in a separate host process from Visual Studio, which makes it more stable and compatible with future VS versions.

Under the hood it uses three core libraries:

- **[Makaretu.Dns.Multicast](https://github.com/richardschneider/net-mdns)** for mDNS/Bonjour service discovery. v1 used [Zeroconf](https://github.com/novotnyllc/Zeroconf), which performed a one-shot scan per discovery session — requiring a new scan each time the tool window was opened and missing devices that announced themselves between scans. Makaretu.Dns.Multicast exposes a lower-level multicast socket API that allows a single passive listener to run for the entire VS session lifetime, continuously forwarding `_adb-tls-pairing._tcp` and `_adb-tls-connect._tcp` announcements to all open tool windows via channels. Credit to [novotnyllc/Zeroconf](https://github.com/novotnyllc/Zeroconf) for powering the original v1 implementation.
- **[QRCoder.Xaml](https://github.com/codebude/QRCoder/)** to generate the pairing QR code rendered as a WPF path geometry.
- **[AdvancedSharpAdbClient](https://github.com/SharpAdb/AdvancedSharpAdbClient)** for pairing, connecting and disconnecting devices. Low-level ADB wire protocol commands (device transport selection, `tcpip:`, `usb:`, `host:track-devices`, `host:disconnect`) are implemented directly over TCP sockets where the library's higher-level API does not apply.

USB device tracking uses `host:track-devices` to stream real-time plug/unplug events. Device model names are read via `getprop ro.product.model` and wireless ADB state via `getprop service.adb.tcp.port` — both fetched in parallel when a USB device appears.

## Contact

If you have any questions, feedback or a bug to report, feel free to open an issue 😊

## Credits

- [SharpAdb/AdvancedSharpAdbClient](https://github.com/SharpAdb/AdvancedSharpAdbClient) — .NET library for communicating with Android devices over ADB

- [codebude/QRCoder](https://github.com/codebude/QRCoder/) — pure C# QR code implementation

- [richardschneider/net-mdns (Makaretu.Dns.Multicast)](https://github.com/richardschneider/net-mdns) — multicast DNS / mDNS for .NET, used from v2.0 onwards

- [novotnyllc/Zeroconf](https://github.com/novotnyllc/Zeroconf) — Bonjour/mDNS library that powered v1 discovery

- Inspired by: [eeriemyxi/lyto](https://github.com/eeriemyxi/lyto) — automatic wireless ADB connection using QR codes