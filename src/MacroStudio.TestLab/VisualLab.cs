using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MacroStudio.TestLab;
public sealed class VisualLab : Window
{
    public VisualLab(string? probe=null,bool scenario=false,string? gate=null) {
        Title="MacroStudio Visual Lab";Width=900;Height=600;Background=Brushes.White;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var panel=new DockPanel{Margin=new(20)};Content=panel;
        var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);panel.Children.Add(header);header.Children.Add(new TextBlock{Text="Visual automation lab",FontSize=25,FontWeight=FontWeights.Bold});
        var ocr=new TextBlock{Text="AUTOMATION READY 42",FontFamily=new FontFamily("Arial"),FontSize=30,Foreground=Brushes.Black,Background=Brushes.White,Margin=new(0,12,0,12)};header.Children.Add(ocr);
        var entry=new TextBox{Height=38,FontSize=20};header.Children.Add(entry);
        var tools=new WrapPanel();header.Children.Add(tools);var canvas=new Canvas{Background=Brushes.White};panel.Children.Add(canvas);
        var target=new Button{Width=170,Height=70,Background=Brushes.Navy,Foreground=Brushes.White,FontSize=20,FontWeight=FontWeights.Bold,Content="◆ TARGET 7 ◆"};canvas.Children.Add(target);Canvas.SetLeft(target,35);Canvas.SetTop(target,80);
        target.FocusVisualStyle=null;
        target.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='#172D75' BorderBrush='#E79024' BorderThickness='4' CornerRadius='3'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>");
        int clicks=0;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};
        void Later(int ms,Action action){var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};t.Tick+=(_,_)=>{t.Stop();action();};t.Start();}
        void Move(){Canvas.SetLeft(target,Canvas.GetLeft(target)<200?450:35);}
        void Tool(string name,Action action){var b=new Button{Content=name,Padding=new(12,6,12,6),Margin=new(2)};b.Click+=(_,_)=>action();tools.Children.Add(b);}
        Tool("Move target",Move);Tool("Appear after 2 seconds",()=>{target.Visibility=Visibility.Hidden;Later(2000,()=>target.Visibility=Visibility.Visible);});Tool("Hide target",()=>target.Visibility=Visibility.Hidden);Tool("Show target",()=>target.Visibility=Visibility.Visible);
        target.Click+=(_,_)=>{if(scenario&&gate!=null&&!File.Exists(gate))return;clicks++;if(scenario){target.Visibility=Visibility.Hidden;if(clicks==1)Later(2500,()=>target.Visibility=Visibility.Visible);}};
        if(probe!=null){timer.Tick+=(_,_)=>{try{ScreenRect Bounds(FrameworkElement e){var p=e.PointToScreen(new Point(0,0));var q=e.PointToScreen(new Point(e.ActualWidth,e.ActualHeight));return new((int)p.X,(int)p.Y,(int)(q.X-p.X),(int)(q.Y-p.Y));}var state=new{Target=Bounds(target),Ocr=Bounds(ocr),Entry=Bounds(entry),Clicks=clicks,Visible=target.IsVisible,Text=entry.Text,Dpi=VisualTreeHelper.GetDpi(this).PixelsPerInchX};File.WriteAllText(probe+".tmp",JsonSerializer.Serialize(state));File.Move(probe+".tmp",probe,true);}catch(IOException){}};}
        Loaded+=(_,_)=>{timer.Start();if(scenario){if(gate is null)Later(2000,Move);else{var wait=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};wait.Tick+=(_,_)=>{if(File.Exists(gate)){wait.Stop();Later(2000,Move);}};wait.Start();Closed+=(_,_)=>wait.Stop();}}};Closed+=(_,_)=>timer.Stop();
    }
    record ScreenRect(int X,int Y,int Width,int Height);
}
