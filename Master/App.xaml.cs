using System.Configuration;
using System.Data;
using System.Windows;

namespace Master;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        MainWindow window = new MainWindow();
        MainWindow = window;
        MainWindow.Show();
        base.OnStartup(e);
    }
}