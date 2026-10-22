using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
namespace MacroStudio.TestLab;
public partial class LabWindow : Window {
    int count,wheels;string wheelTarget="";bool dragging;Point offset;
    public LabWindow(){InitializeComponent();for(int i=1;i<=30;i++)ScrollItems.Children.Add(new TextBlock(){Text=$"Scroll item {i:00}",Margin=new(10,7,10,7)});PreviewKeyDown+=(_,e)=>Keys.Text=$"{Keyboard.Modifiers} + {e.Key}";PreviewMouseDown+=(_,e)=>Events.Text=$"{e.ChangedButton} · clicks {e.ClickCount}";
        PreviewMouseWheel+=(_,e)=>{wheels++;wheelTarget=$"{e.OriginalSource?.GetType().Name}/{Mouse.DirectlyOver?.GetType().Name}";};
        var args=Environment.GetCommandLineArgs();int probe=Array.IndexOf(args,"--probe");
        // Optional read-only telemetry for this local test target; no command receiver or network listener.
        if(probe>=0&&probe+1<args.Length){var path=Path.GetFullPath(args[probe+1]);var timer=new DispatcherTimer(){Interval=TimeSpan.FromMilliseconds(30)};timer.Tick+=(_,_)=>{try{int[] Center(FrameworkElement element){var p=element.PointToScreen(new Point(element.ActualWidth/2,element.ActualHeight/2));return [(int)p.X,(int)p.Y];}var state=new{Text=Entry.Text,Count=count,DragX=Canvas.GetLeft(Draggable),Status=Events.Text,Scroll=Scroller.VerticalOffset,Wheels=wheels,WheelTarget=wheelTarget,ScrollMaximum=Scroller.ScrollableHeight,Entry=Center(Entry),Counter=Center(Counter),Reset=Center(ResetButton),Drag=Center(Draggable),Drop=Center(DropTarget),ScrollPoint=Center(Scroller),Dpi=System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX};File.WriteAllText(path+".tmp",JsonSerializer.Serialize(state));File.Move(path+".tmp",path,true);}catch(IOException){}};Loaded+=(_,_)=>timer.Start();Closed+=(_,_)=>timer.Stop();}
    }
    void Count(object s,RoutedEventArgs e)=>Counter.Content=$"Click counter: {++count}";
    void Reset(object s,RoutedEventArgs e){Entry.Clear();SecondEntry.Clear();count=0;Counter.Content="Click counter: 0";Canvas.SetLeft(Draggable,30);Canvas.SetTop(Draggable,50);Scroller.ScrollToTop();Events.Text="Lab reset";}
    void DragDown(object s,MouseButtonEventArgs e){dragging=true;offset=e.GetPosition(Draggable);DragCanvas.CaptureMouse();e.Handled=true;}
    void DragMove(object s,MouseEventArgs e){if(!dragging)return;var p=e.GetPosition(DragCanvas);Canvas.SetLeft(Draggable,p.X-offset.X);Canvas.SetTop(Draggable,p.Y-offset.Y);}
    void DragUp(object s,MouseButtonEventArgs e){dragging=false;DragCanvas.ReleaseMouseCapture();Events.Text=Canvas.GetLeft(Draggable)>440?"Drag target reached":"Drag released";}
}
