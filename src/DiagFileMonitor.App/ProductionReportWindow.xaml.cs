using System.Windows;

namespace DiagFileMonitor.App;

public partial class ProductionReportWindow : Window
{
    public ProductionReportWindow()
    {
        InitializeComponent();

        // Press ?, then click anything to find out what it does.
        DiagFileMonitor.App.Help.HelpMode.Attach(this, HelpModeButton);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
