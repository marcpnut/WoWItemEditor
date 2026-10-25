using System.Windows;
namespace WoWItemEditor;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new MainWindow(e.Args.Contains("--verify-set-creator")||e.Args.Contains("--verify-storage")||e.Args.Contains("--verify-settings")||e.Args.Contains("--verify-startup")||e.Args.Contains("--verify")||e.Args.Contains("--verify-equipment")||e.Args.Contains("--verify-set-review")||e.Args.Contains("--verify-models")||e.Args.Contains("--verify-character-model")||e.Args.Contains("--verify-races")||e.Args.Contains("--verify-skins")||e.Args.Contains("--verify-saved-appearances")).Show();
    }
}
