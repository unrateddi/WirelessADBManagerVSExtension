using Microsoft.VisualStudio.Extensibility.UI;

namespace WirelessADBManagerVSExtension;

/// <summary>
/// A remote user control to use as tool window UI content.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="WirelessADBManagerToolWindowContent" /> class.
/// </remarks>
public class WirelessADBManagerToolWindowContent(WirelessADBManagerToolWindowData dataContext, SynchronizationContext? synchronizationContext = null) : RemoteUserControl(dataContext, synchronizationContext)
{
    private readonly WirelessADBManagerToolWindowData _dataContext = dataContext;
    public override async Task ControlLoadedAsync(CancellationToken cancellationToken)
    {
        await base.ControlLoadedAsync(cancellationToken);

        await _dataContext.DiscoverDevicesAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dataContext.Dispose();
        }

        base.Dispose(disposing);
    }
}
