using System.Windows;

namespace DiagFileMonitor.App;

public partial class LogSearchWindow : Window
{
    public LogSearchWindow()
    {
        InitializeComponent();

        // Press ?, then click anything to find out what it does.
        DiagFileMonitor.App.Help.HelpMode.Attach(this, HelpModeButton);
    }
}
