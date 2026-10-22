using System.Windows;
namespace MacroStudio.TestLab;public partial class App : Application {
    protected override void OnStartup(StartupEventArgs e){base.OnStartup(e);int i=Array.IndexOf(e.Args,"--probe"),g=Array.IndexOf(e.Args,"--start-gate");MainWindow=e.Args.Contains("--visual")?new VisualLab(i>=0&&i+1<e.Args.Length?e.Args[i+1]:null,e.Args.Contains("--scenario"),g>=0&&g+1<e.Args.Length?e.Args[g+1]:null):new LabWindow();MainWindow.Show();}
}
