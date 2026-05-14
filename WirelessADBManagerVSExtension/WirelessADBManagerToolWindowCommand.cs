using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace WirelessADBManagerVSExtension;

/// <summary>
/// A command for showing a tool window.
/// </summary>
[VisualStudioContribution]
public class WirelessADBManagerToolWindowCommand : Command
{
    /// <summary>
    /// A dedicated group placed in the Tools menu at priority 0x0600, replicating
    /// MyMenuGroup from the legacy package.vsct (guidWirelessADBManagerVSExtensionPackageCmdSet / 0x1020).
    /// </summary>
    [VisualStudioContribution]
    public static CommandGroupConfiguration WirelessADBManagerToolsMenuGroup => new(GroupPlacement.KnownPlacements.ToolsMenu.WithPriority(0x0600))
    {
        Children =
        [
            GroupChild.Command<WirelessADBManagerToolWindowCommand>(),
        ],
    };

    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration => new(displayName: "%WirelessADBManagerVSExtension.OpenWirelessADBManagerCommand.DisplayName%")
    {
        Icon = new(ImageMoniker.KnownValues.Phone, IconSettings.IconAndText),
    };

    /// <inheritdoc />
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await this.Extensibility.Shell().ShowToolWindowAsync<WirelessADBManagerToolWindow>(activate: true, cancellationToken);
    }
}
