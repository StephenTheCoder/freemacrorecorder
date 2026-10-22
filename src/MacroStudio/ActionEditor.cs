using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MacroStudio.Core;

namespace MacroStudio;
public sealed class ActionEditor : Window
{
    public MacroAction? Result {get;private set;}
    public ActionEditor(MacroAction source) {
        SetResourceReference(BackgroundProperty,"Bg");SetResourceReference(ForegroundProperty,"Ink");FontFamily=new System.Windows.Media.FontFamily("Segoe UI");FontSize=13;
        Title="Edit step properties";Width=620;Height=760;MinWidth=500;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel(){Margin=new(24)};Content=root;var footer=new StackPanel(){Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var cancel=new Button(){Content="Cancel",IsCancel=true};var apply=new Button(){Content="Apply step",IsDefault=true};footer.Children.Add(cancel);footer.Children.Add(apply);
        var panel=new StackPanel();root.Children.Add(new ScrollViewer(){Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        void Label(string text)=>panel.Children.Add(new TextBlock(){Text=text,Margin=new(0,6,0,0)});
        TextBox Field(string name,string value,bool multiline=false){Label(name);var box=new TextBox(){Text=value,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,MinHeight=multiline?72:0};System.Windows.Automation.AutomationProperties.SetName(box,name);panel.Children.Add(box);return box;}
        Label("Action type");var kind=new ComboBox(){ItemsSource=Enum.GetValues<ActionKind>(),SelectedItem=source.Kind};panel.Children.Add(kind);
        var enabled=new CheckBox(){Content="Enabled",IsChecked=source.Enabled};panel.Children.Add(enabled);
        var delay=Field("Delay before action (ms)",source.DelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var duration=Field("Duration (ms) — delay or path",source.DurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var text=Field("Unicode text",source.Text,true);
        var comment=Field("Comment",source.Comment);
        Label("Coordinate mode");var coordinates=new ComboBox(){ItemsSource=Enum.GetValues<CoordinateMode>(),SelectedItem=source.Coordinates};panel.Children.Add(coordinates);
        var x=Field("X (physical pixels)",source.X.ToString());var y=Field("Y (physical pixels)",source.Y.ToString());
        Label("Mouse button");var button=new ComboBox(){ItemsSource=Enum.GetValues<MacroStudio.Core.MouseButton>(),SelectedItem=source.Button};panel.Children.Add(button);
        var down=new CheckBox(){Content="Down (unchecked means up)",IsChecked=source.IsDown};panel.Children.Add(down);
        var wheel=Field("Scroll delta (120 = one notch)",source.WheelDelta.ToString());var horizontal=new CheckBox(){Content="Horizontal scroll",IsChecked=source.Horizontal};panel.Children.Add(horizontal);
        var vk=Field("Virtual key code (decimal, e.g. 65 for A)",source.VirtualKey.ToString());var scan=Field("Scan code (0 uses virtual key)",source.ScanCode.ToString());var extended=new CheckBox(){Content="Extended key",IsChecked=source.Extended};panel.Children.Add(extended);
        var keys=Field("Key chord (comma-separated decimal codes, modifiers first)",string.Join(",",source.Keys));
        var path=Field("Mouse path JSON [{\"x\":0,\"y\":0,\"offsetMs\":0}, …]",JsonSerializer.Serialize(source.Path,MacroStore.Json),true);
        var process=Field("Target process name (without .exe; blank = none)",source.Target?.ProcessName??"");var title=Field("Target window title (exact preferred; unique process fallback)",source.Target?.Title??"");
        var left=Field("Recorded window left",(source.Target?.Left??0).ToString());var top=Field("Recorded window top",(source.Target?.Top??0).ToString());var width=Field("Recorded window width",(source.Target?.Width??900).ToString());var height=Field("Recorded window height",(source.Target?.Height??700).ToString());
        var error=new TextBlock(){TextWrapping=TextWrapping.Wrap,Foreground=System.Windows.Media.Brushes.IndianRed};panel.Children.Add(error);
        apply.Click+=(_,_)=>{try{var a=MacroStore.Clone(source);a.Kind=(ActionKind)kind.SelectedItem;a.Enabled=enabled.IsChecked==true;a.DelayMs=double.Parse(delay.Text,System.Globalization.CultureInfo.InvariantCulture);a.DurationMs=double.Parse(duration.Text,System.Globalization.CultureInfo.InvariantCulture);a.Text=text.Text;a.Comment=comment.Text;a.Coordinates=(CoordinateMode)coordinates.SelectedItem;a.X=int.Parse(x.Text);a.Y=int.Parse(y.Text);a.Button=(MacroStudio.Core.MouseButton)button.SelectedItem;a.IsDown=down.IsChecked==true;a.WheelDelta=int.Parse(wheel.Text);a.Horizontal=horizontal.IsChecked==true;a.VirtualKey=int.Parse(vk.Text);a.ScanCode=int.Parse(scan.Text);a.Extended=extended.IsChecked==true;a.Keys=keys.Text.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(int.Parse).ToList();a.Path=JsonSerializer.Deserialize<List<PathPoint>>(path.Text,MacroStore.Json)??[];a.Target=string.IsNullOrWhiteSpace(process.Text)?null:new(process.Text.Trim(),title.Text,int.Parse(left.Text),int.Parse(top.Text),int.Parse(width.Text),int.Parse(height.Text));MacroStore.Validate(new(){Actions=[a]});Result=a;DialogResult=true;}catch(Exception ex){error.Text=ex.Message;error.BringIntoView();}};
    }
}
