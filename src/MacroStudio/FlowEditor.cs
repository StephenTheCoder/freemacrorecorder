using System.Windows;
using System.Windows.Controls;
using MacroStudio.Core;

namespace MacroStudio;
public sealed class FlowEditor : Window
{
    public FlowSettings Result {get;private set;}
    public FlowEditor(MacroAction action,IEnumerable<string> labels){
        WindowTheme.Apply(this);Result=MacroStore.Clone(action).Flow;Title="Flow control and timeout handling";Width=590;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new(22)};Content=root;var apply=new Button{Content="Apply flow settings"};DockPanel.SetDock(apply,Dock.Bottom);root.Children.Add(apply);var panel=new StackPanel();root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        TextBox Field(string name,string value){panel.Children.Add(new TextBlock{Text=name,TextWrapping=TextWrapping.Wrap});var b=new TextBox{Text=value};System.Windows.Automation.AutomationProperties.SetName(b,name);panel.Children.Add(b);return b;}
        var label=Field("Label name (Label action)",Result.Label);
        ComboBox Destination(string name,string value){panel.Children.Add(new TextBlock{Text=name});var box=new ComboBox{IsEditable=true,ItemsSource=labels.ToList(),Text=value};panel.Children.Add(box);return box;}
        var destination=Destination("Destination label (Jump / true branch / timeout jump)",Result.Destination);var otherwise=Destination("False branch label (blank continues)",Result.Otherwise);
        var count=Field("Bounded repeat count (1–1000000)",Result.Count.ToString());var file=Field("Reusable macro file (relative to this macro; static path)",Result.MacroFile);
        panel.Children.Add(new TextBlock{Text="Detection timeout policy"});var policy=new ComboBox{ItemsSource=Enum.GetValues<TimeoutPolicy>(),SelectedItem=Result.Timeout};panel.Children.Add(policy);var retries=Field("Additional retries before stop (0–100)",Result.Retries.ToString());
        panel.Children.Add(new TextBlock{Text="Pair RepeatStart and RepeatEnd. Branch conditions use Value, Operation and Right operand under data settings. Jumps must stay in the same repeat block. Calls share variables and are validated before playback.",TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,12)});
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap};panel.Children.Add(error);apply.Click+=(_,_)=>{try{Result.Label=label.Text.Trim();Result.Destination=destination.Text.Trim();Result.Otherwise=otherwise.Text.Trim();Result.Count=int.Parse(count.Text);Result.MacroFile=file.Text;Result.Timeout=(TimeoutPolicy)policy.SelectedItem;Result.Retries=int.Parse(retries.Text);MacroStore.Validate(new(){Actions=[new(){Flow=Result}]});DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}};
    }
}
