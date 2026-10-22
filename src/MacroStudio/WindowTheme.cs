using System.Windows;
using System.Windows.Media;
namespace MacroStudio;
public static class WindowTheme
{
    public static void Apply(Window window){window.SetResourceReference(Window.BackgroundProperty,"Bg");window.SetResourceReference(Window.ForegroundProperty,"Ink");window.FontFamily=new FontFamily("Segoe UI");window.FontSize=13;}
}
