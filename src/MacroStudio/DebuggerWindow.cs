using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MacroStudio.Core;

namespace MacroStudio;
public sealed class DebuggerWindow : Window
{
    public DebuggerWindow(StudioViewModel vm){
        WindowTheme.Apply(this);Title="Execution inspector";Width=800;Height=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new(20)};Content=root;var toolbar=new WrapPanel();DockPanel.SetDock(toolbar,Dock.Top);root.Children.Add(toolbar);
        var pause=new Button{Content="Pause / resume"};pause.Click+=(_,_)=>vm.Engine.TogglePause();toolbar.Children.Add(pause);var stop=new Button{Content="Stop"};stop.Click+=(_,_)=>vm.Engine.Stop();toolbar.Children.Add(stop);
        var step=new Button{Content="Single step"};step.Click+=(_,_)=>vm.Engine.Step();toolbar.Children.Add(step);var state=new TextBlock{Margin=new(6),VerticalAlignment=VerticalAlignment.Center};toolbar.Children.Add(state);
        var grid=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true};grid.Columns.Add(new DataGridTextColumn{Header="Variable",Binding=new System.Windows.Data.Binding("Name"),Width=150});grid.Columns.Add(new DataGridTextColumn{Header="Type",Binding=new System.Windows.Data.Binding("Type"),Width=100});grid.Columns.Add(new DataGridTextColumn{Header="Value",Binding=new System.Windows.Data.Binding("Value"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});var tabs=new TabControl();root.Children.Add(tabs);tabs.Items.Add(new TabItem{Header="Variables",Content=grid});var logPanel=new DockPanel();var search=new TextBox{ToolTip="Search execution logs"};DockPanel.SetDock(search,Dock.Top);logPanel.Children.Add(search);var logList=new ListBox{FontFamily=new System.Windows.Media.FontFamily("Consolas"),FontSize=12};logPanel.Children.Add(logList);tabs.Items.Add(new TabItem{Header="Execution log",Content=logPanel});
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};timer.Tick+=(_,_)=>{logList.ItemsSource=vm.Engine.Logs.Where(l=>$"{l.Level} {l.Message} {l.Action}".Contains(search.Text,StringComparison.OrdinalIgnoreCase)).Select(l=>$"{l.Time:HH:mm:ss.fff} {l.Level,-8} {l.Action.ToString()[..8]} {l.Message}").ToList();state.Text=$"{vm.Session.State} — pause to inspect a stable snapshot";grid.ItemsSource=vm.Engine.Context?.Variables.Snapshot().Select(v=>new{Name=v.Key,Type=v.Value.Type.ToString(),v.Value.Value}).ToList();};Loaded+=(_,_)=>timer.Start();Closed+=(_,_)=>timer.Stop();
    }
}
