# Wireless ADB Manager for Visual Studio

Wireless ADB Manager is a Visual Studio extension that helps you pair and connect Android devices for Wireless Debugging easily. The most simple use-case scenario is to connect an Android device by scanning the QR-Code produced by this extension. While this is still in Preview version this scenario should work as expected. On top of that there is a discovery mechanism to provide feedback of which devices have Wireless Debugging on, what is the state of the connection and the action a user can do. The extension also supports Manual Pairing by Pairing Code, directly Connect to already paired devices without having to scan a QR-Code or enter a Pairing Code again and also Disconnect a Connected device.



## What you can do

- Easily manage your Wireless ADB Connections directly through Visual Studio

- Pair and Connect Android devices by scanning a QR-Code for Wireless Debugging

- Pair and Connect Android devices by entering the Pairing Code Wireless Debugging provide

- Direcltly Connect to an already paired device

- Disconnect a Connected device



## Usage

The Wireless ADB Manager can be found under the Tools menu in Visual Studio. No USB is need to use this extension, everything is handled wirelessly. The PC and the Android devices must be in the same network. On the Android device head to Developer Settings (Developer Settings should be enabled) and then go to Wireless Debugging menu. After enabling Wireless Debugging follow the steps of Pair/Connect method of your choice.

#### Pair and Connect with QR-Code

The Wireless ADB Manager automatically provides a QR-Code ready to scan and pair fast and easy. On the Android device in the Wireless Debugging menu tap on the "Pair device with QR code" option (wording might differ from device to device). A QR code scanner should appear on the screen with which you should scan the QR provided by the Wireless ADB Manager. After that the pairing process should start and complete with connecting the PC's ADB server to the Android device. Then the Android device should be selectable from Visual Studio as a local Android device in the same way it would be if it was connected via USB.

#### Pair and Connect with Pairing Code

On your Android device in the Wireless Debugging tap on the "Pair device with pairing code" option (wording might differ from device to device). This should bring a popup with the pairing information like the device's IP, pairing port and pairing code. This should also announce that the device is in pairing mode to the network. After that your Android device should show up in the Wireless ADB Manager window's discovery list with the button's action being "Pair". Hitting "Pair" will bring up a dialog to enter the pairing code your Android device displays. The pairing code must be 6 digits. By pressing "Enter" or hitting the Ok button the Wireless ADB Manager will pair with the Android device.

#### Connect to an already paired device

While scanning the QR code each time is not that big of an effort and should work each time also if a device is already paired, the extension provides the functionality to directly send a connect request to a device found in the discovery list. All discovered devices that appear in the discovert list by default display the "Connect" button. An Android device must announce itself to be discovered thus must be in the Wireless Debugging menu with Wireless Debugging enabled. No other other action should be needed on the Android device. If the device has been paired with the PC before then the connect request should succeed. If not, the state of the button in the Wireless ADB Manager won't change.

#### Disconnect a device

After successfully pairing and connecting or directly connecting to an Android device the corresponding entry in the discovery list should show the "Disconnect" button. By hitting "Discconet" Wireless ADB Manager will disconnect the session with that device. Disconnecting doesn't remove the pairing between the PC and the Android device and should be able to Connect again instantly.



## How it works

Under the hood Wireless ADB Manager uses two core libraries, [Zeroconf](https://github.com/novotnyllc/Zeroconf) and [AdvancedSharpAdbClient](https://github.com/SharpAdb/AdvancedSharpAdbClient). Zeroconf is used to browse Android's pairing and connect service announcements. AdvancedSharpAdbClient is used to easily perform the ADB commands for pairing, connecting and disconnecting to the devices. Each device discovered is shown in the Discovery list with the state / button-action calculated based on the service type it discovered under.



## Contact

If you have any questions, feedback or a bug to report, feel free to open an issue 😊



## Credits

- [SharpAdb/AdvancedSharpAdbClient: AdvancedSharpAdbClient is a .NET library that allows .NET, Mono and Unity applications to communicate with Android devices. It&#39;s improved version of SharpAdbClient.](https://github.com/SharpAdb/AdvancedSharpAdbClient)

- [novotnyllc/Zeroconf: Bonjour support for .NET Core, .NET 4.6, Xamarin, and UWP](https://github.com/novotnyllc/Zeroconf)

- Inspired by: [eeriemyxi/lyto: Automatic wireless ADB connection using QR codes.](https://github.com/eeriemyxi/lyto)