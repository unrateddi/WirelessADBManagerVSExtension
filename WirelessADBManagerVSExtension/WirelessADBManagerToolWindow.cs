using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;
using WirelessADBManagerVSExtension.Services;

namespace WirelessADBManagerVSExtension
{
    /// <summary>
    /// A sample tool window.
    /// </summary>
    [VisualStudioContribution]
    public class WirelessADBManagerToolWindow : ToolWindow
    {
        //private readonly WirelessADBManagerToolWindowContent _content;
        private readonly VisualStudioExtensibility _extensibility;
        private readonly WirelessAdbManagerService _wirelessAdbManagerService;
        private readonly AdbService _adbService;

        /// <summary>
        /// Initializes a new instance of the <see cref="WirelessADBManagerToolWindow" /> class.
        /// </summary>
        public WirelessADBManagerToolWindow(VisualStudioExtensibility extensibility, WirelessAdbManagerService wirelessAdbManagerService, AdbService adbService) : base(extensibility)
        {
            //_content = new WirelessADBManagerToolWindowContent(new WirelessADBManagerToolWindowData(wirelessAdbManagerService));
            _extensibility = extensibility;
            _wirelessAdbManagerService = wirelessAdbManagerService;
            _adbService = adbService;

            Title = "Wireless ADB Manager";
        }

        /// <inheritdoc />
        public override ToolWindowConfiguration ToolWindowConfiguration => new()
        {
            // Use this object initializer to set optional parameters for the tool window.
            Placement = ToolWindowPlacement.Floating,
        };

        /// <inheritdoc />
        public override async Task InitializeAsync(CancellationToken cancellationToken)
        {
            // Use InitializeAsync for any one-time setup or initialization.
            await _adbService.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IRemoteUserControl>(new WirelessADBManagerToolWindowContent(new WirelessADBManagerToolWindowData(_wirelessAdbManagerService, _extensibility)));
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}
