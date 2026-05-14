using AdvancedSharpAdbClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;
using WirelessADBManagerVSExtension.Services;

namespace WirelessADBManagerVSExtension;

/// <summary>
/// Extension entrypoint for the VisualStudio.Extensibility extension.
/// </summary>
[VisualStudioContribution]
internal class ExtensionEntrypoint : Extension
{
    /// <inheritdoc/>
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
                id: "WirelessADBManagerVSExtension.815be562-f02e-41a6-afd5-66f28aa2b851",
                version: this.ExtensionAssemblyVersion,
                publisherName: "Dimitrios Iliopoulos",
                displayName: "Wireless ADB Manager",
                description: "Enables quick pair and connect to Android devices via QR-Code or Pairing-Code for Wireless Debugging.")
        {
            Tags = ["android", "adb", "wireless"],
            Preview = false,
        },
    };

    /// <inheritdoc />
    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);

        serviceCollection.AddSettingsObservers();

        // You can configure dependency injection here by adding services to the serviceCollection.
        serviceCollection.AddSingleton<WirelessAdbManagerService>();
        serviceCollection.AddSingleton<AdbService>();

        serviceCollection.AddSingleton<IAdbClient, AdbClient>();
    }
}
