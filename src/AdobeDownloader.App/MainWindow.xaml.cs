using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AdobeDownloader.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnOpenCleanup(object sender, RoutedEventArgs e)
    {
        try
        {
            new CleanupWindow { Owner = this }.Show();
        }
        catch (Exception ex)
        {
            AppDialog.Show(ex.ToString(), "打开清理窗口失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenUninstall(object sender, RoutedEventArgs e)
    {
        try
        {
            new UninstallWindow { Owner = this }.Show();
        }
        catch (Exception ex)
        {
            AppDialog.Show(ex.ToString(), "打开卸载窗口失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}