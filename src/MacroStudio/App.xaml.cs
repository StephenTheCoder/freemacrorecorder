using System.Windows;
namespace MacroStudio;
public partial class App : Application {
    protected override async void OnStartup(StartupEventArgs e) {
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((s,_)=>{if(s is Window w)MacroStudio.Windows.ScreenCapture.Exclude(w);}));
        base.OnStartup(e);
        if(e.Args.Any(a=>a.StartsWith("--"))){ShutdownMode=ShutdownMode.OnExplicitShutdown;Shutdown(await CommandLine.RunAsync(e.Args));return;}
        MainWindow=new MainWindow();MainWindow.Show();
    }
}
