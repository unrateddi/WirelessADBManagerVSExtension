using System.ComponentModel;
using System.Windows;
using WirelessADBManagerVSExtension.Models;
using WirelessADBManagerVSExtension.ViewModels;

namespace WirelessADBManagerVSExtension.Views;

/// <summary>
/// Interaction logic for WirelessADBManagerView.xaml
/// </summary>
public partial class WirelessADBManagerView : Window
{
    public WirelessADBManagerView()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        (DataContext as WirelessAdbManagerViewModel).CancelAll();
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        await (DataContext as WirelessAdbManagerViewModel).DecideActionAsync(((FrameworkElement)e.OriginalSource).DataContext as DeviceInfo);
    }
}
