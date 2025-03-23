using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Shell;
using WirelessADBManagerVSExtension.Utils;
using System.IO;

namespace WirelessADBManagerVSExtension.Services;

public class AdbService
{
    const string DefaultAdbPath = "C:\\Program Files (x86)\\Android\\android-sdk\\platform-tools\\adb.exe";
    const string AdbPathLocator = "android-sdk\\platform-tools";
    const string AdbExecutableName = "adb.exe";

    readonly IServiceProvider _serviceProvider = WirelessADBManagerVSExtensionPackage.Instance;
    readonly IAdbClient _adbClient = new AdbClient();

    readonly string _adbPath;

    internal static bool IsAdbServerRunning => AdbServer.Instance.GetStatus().IsRunning;

    public AdbService()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _adbPath = GetAdbPathFromSettings();

        if (!string.IsNullOrEmpty(_adbPath))
        {
            return;
        }

        var adbFromPath = TryToFindAdbPathInPathEnvironmentVariable();

        if (!string.IsNullOrEmpty(adbFromPath))
        {
            SaveAdbPathToSettings(adbFromPath);
            _adbPath = GetAdbPathFromSettings();
            return;
        }

        SaveAdbPathToSettings(DefaultAdbPath);
        _adbPath = GetAdbPathFromSettings();
    }

    internal async Task<(bool Success, DeviceData ConnectedDevice)> AdbConnectAsync(string ip, int port, CancellationToken cancellationToken)
    {
        await EnsureAdbServerIsRunningAsync(cancellationToken);

        try
        {
            await _adbClient.ConnectAsync(ip, port, cancellationToken);
            var devices = await _adbClient.GetDevicesAsync(cancellationToken);
            var device = devices.FirstOrDefault(d => d.Serial == $"{ip}:{port}");

            if (device == default(DeviceData))
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

    internal async Task<bool> AdbDisconnectAsync(string ip, int port, CancellationToken cancellationToken)
    {
        await EnsureAdbServerIsRunningAsync(cancellationToken);

        try
        {
            var disconnectResult = await _adbClient.DisconnectAsync(ip, port, cancellationToken);

            return !disconnectResult.StartsWith("error");
        }
        catch
        {
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

    private async Task EnsureAdbServerIsRunningAsync(CancellationToken cancellationToken)
    {
        if (!AdbServer.Instance.GetStatus().IsRunning)
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

    private string? GetAdbPathFromSettings()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            SettingsManager settingsManager = new ShellSettingsManager(_serviceProvider);
            SettingsStore userSettingsStore = settingsManager.GetReadOnlySettingsStore(SettingsScope.UserSettings);

            if (userSettingsStore.CollectionExists(SettingsStoreConstants.CollectionKey))
            {
                return userSettingsStore.GetString(SettingsStoreConstants.CollectionKey, SettingsStoreConstants.AdbPathPropertyName, string.Empty);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting ADB path from settings: {ex.Message}");
        }

        return null; // Return null if not found or error
    }

    private void SaveAdbPathToSettings(string adbPath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            SettingsManager settingsManager = new ShellSettingsManager(_serviceProvider);
            WritableSettingsStore userSettingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

            if (!userSettingsStore.CollectionExists(SettingsStoreConstants.CollectionKey))
            {
                userSettingsStore.CreateCollection(SettingsStoreConstants.CollectionKey);
            }

            userSettingsStore.SetString(SettingsStoreConstants.CollectionKey, SettingsStoreConstants.AdbPathPropertyName, adbPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving ADB path to settings: {ex.Message}");
        }
    }

    private static string TryToFindAdbPathInPathEnvironmentVariable()
    {
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
        var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);

        List<string> paths = [.. userPath.Split(';').Concat(machinePath.Split(';')).Distinct()];

        var adbPath = paths.FirstOrDefault(p => p.Contains(AdbPathLocator));

        if (string.IsNullOrWhiteSpace(adbPath))
            return null;

        return Path.Combine(adbPath, AdbExecutableName);
    }

    #endregion

}
