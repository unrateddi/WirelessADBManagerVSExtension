using Microsoft.VisualStudio.PlatformUI;
using System.Windows;
using System.Windows.Media;
using WirelessADBManagerVSExtension.Helpers;

namespace WirelessADBManagerVSExtension.Views
{
    /// <summary>
    /// Interaction logic for ManualPairingCodePrompt.xaml
    /// </summary>
    public partial class ManualPairingCodePromptView : Window
    {
        public string UserEnteredPairingCode { get; set; }

        public ManualPairingCodePromptView()
        {
            InitializeComponent();

            Background = new SolidColorBrush(ColorHelpers.GetVsThemeColor(EnvironmentColors.ToolWindowBackgroundColorKey, Colors.White));

            var textBrush = new SolidColorBrush(ColorHelpers.GetVsThemeColor(EnvironmentColors.ToolWindowTextBrushKey, Colors.Black));

            HintTextBlock.Foreground = PairingCodeLabel.Foreground = PairingCodeTextBox.Foreground = OkButton.Foreground = textBrush;

            HintTextBlock.Background = PairingCodeTextBox.Background = OkButton.Background = new SolidColorBrush(Colors.Transparent);

            PairingCodeTextBox.Focus();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            UserEnteredPairingCode = PairingCodeTextBox.Text;
            DialogResult = true;
        }

        private void PairingCodeTextBox_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter)
                return;

            if (PairingCodeTextBox.Text.Length != 6)
                return;

            UserEnteredPairingCode = PairingCodeTextBox.Text;
            DialogResult = true;
        }

        private void PairingCodeTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            OkButton.IsEnabled = PairingCodeTextBox.Text.Length == 6;
        }
    }
}
