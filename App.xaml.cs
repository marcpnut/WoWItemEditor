using System.Windows;
namespace AnoWowItemEditor;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new MainWindow(e.Args.Contains("--verify")||e.Args.Contains("--verify-equipment")).Show();
    }
}
