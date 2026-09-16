using System.Windows;

namespace DiagFileMonitor.App;

public partial class IntegrationSettingsWindow : Window
{
    public IntegrationSettingsWindow()
    {
        InitializeComponent();

        // Press ?, then click anything to find out what it does.
        DiagFileMonitor.App.Help.HelpMode.Attach(this, HelpModeButton);
    }
}
