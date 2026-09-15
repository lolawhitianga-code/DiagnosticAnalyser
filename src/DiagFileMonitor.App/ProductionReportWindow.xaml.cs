using System.Windows;

namespace DiagFileMonitor.App;

public partial class ProductionReportWindow : Window
{
    public ProductionReportWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
