using System.Windows;

namespace PanelNester.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow(StartupProjectPathResolver.Resolve(e.Args));
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
