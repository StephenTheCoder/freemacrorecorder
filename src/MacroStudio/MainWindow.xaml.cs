using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MacroStudio.Core;
using MacroStudio.Windows;

namespace MacroStudio;
public partial class MainWindow : Window
{
    StudioViewModel vm=null!;
    ControllerWindow controller=null!;
    readonly Preferences preferences=Preferences.Load();
    readonly LocalLibrary library=new();
    Point dragOrigin;
    bool closeApproved,closingBusy;
    TextBox quickDelay=null!,quickDuration=null!,quickText=null!,quickComment=null!;
    StackPanel quickPanel=null!;
    public MainWindow() {
        InitializeComponent();PathShapePicker.ItemsSource=Enum.GetValues<MousePathShape>();
        CreateInlineInspector();
        Loaded+=async(_,_)=> {
            try {
                vm=new();DataContext=vm;System.Windows.Data.CollectionViewSource.GetDefaultView(vm.Rows).GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Section"));controller=new(vm,Safe);
                vm.RestoreSelection+=ids=>{ActionGrid.SelectedItems.Clear();foreach(var id in ids)if(vm.Rows.FirstOrDefault(r=>r.Action.Id==id) is {} row)ActionGrid.SelectedItems.Add(row);};
                vm.Hooks.Control+=command=>Dispatcher.BeginInvoke(()=>Safe(async()=> {
                    if(command is "Record" or "Play" && (!IsEnabled || closingBusy))return;
                    if(command.StartsWith("Library:")){if(vm.Idle&&IsEnabled)await vm.PlayFileAsync(command[8..]);return;}switch(command){case "Record":await RecordCore();break;case "Play":await vm.PlayAsync();break;case "Pause":vm.Engine.TogglePause();break;case "Stop":await vm.StopAsync();break;}
                }));
                vm.Session.Changed+=state=>Dispatcher.BeginInvoke(()=> {if(state!=SessionState.Idle){if(!controller.IsVisible)controller.Show();controller.UpdateState();}else controller.Hide();});
                try{await ConfigureAllHotkeys();}catch(Exception ex){vm.Status="Shortcut setup failed: "+ex.Message;MessageBox.Show(this,vm.Status,"Global controls",MessageBoxButton.OK,MessageBoxImage.Warning);}
                SetTheme(preferences.Dark);UpdateHotkeyHint();RefreshRecent();
                var args=Environment.GetCommandLineArgs();if(args.Length>1 && File.Exists(args[1]))await OpenPath(args[1]);
            }catch(Exception ex){MessageBox.Show(this,ex.ToString(),"Startup failed");Close();}
        };
    }
    async void Safe(Func<Task> work){try{await work();}catch(Exception ex){if(vm!=null)vm.Status="Error: "+ex.Message;MessageBox.Show(this,ex.Message,"MacroStudio",MessageBoxButton.OK,MessageBoxImage.Error);}}
    Task Do(Action action){action();return Task.CompletedTask;}
    async Task RecordCore(){if(!vm.Idle)return;vm.Recorder.VisualEnabled=VisualRecord.IsChecked==true;vm.RecordCoordinates=CoordinatePicker.SelectedIndex==1?CoordinateMode.Window:CoordinateMode.Desktop;await vm.RecordAsync();}
    void Record(object s,RoutedEventArgs e)=>Safe(RecordCore);
    void Stop(object s,RoutedEventArgs e)=>Safe(vm.StopAsync);
    void Play(object s,RoutedEventArgs e)=>Safe(()=>vm.PlayAsync());
    void Pause(object s,RoutedEventArgs e)=>vm.Engine.TogglePause();
    void PlayOne(object s,RoutedEventArgs e)=>Safe(()=>vm.PlayAsync("one"));
    void PlayRange(object s,RoutedEventArgs e)=>Safe(()=>vm.PlayAsync("range"));
    void PlayFrom(object s,RoutedEventArgs e)=>Safe(()=>vm.PlayAsync("from"));
    void Undo(object s,RoutedEventArgs e)=>vm.Undo();void Redo(object s,RoutedEventArgs e)=>vm.Redo();
    void Delete(object s,RoutedEventArgs e)=>vm.Delete();void ToggleEnabled(object s,RoutedEventArgs e)=>vm.ToggleEnabled();
    void MoveUp(object s,RoutedEventArgs e)=>vm.MoveSelection(-1);void MoveDown(object s,RoutedEventArgs e)=>vm.MoveSelection(1);
    void Copy(object s,RoutedEventArgs e)=>Safe(()=>Do(()=>{if(vm.Selected.Count>0)Clipboard.SetText(vm.Copy());}));
    void Cut(object s,RoutedEventArgs e)=>Safe(()=>Do(()=>{if(vm.Idle&&vm.Selected.Count>0){Clipboard.SetText(vm.Copy());vm.Delete();}}));
    void Paste(object s,RoutedEventArgs e)=>Safe(()=>Do(()=>{if(vm.Idle&&Clipboard.ContainsText())vm.Paste(Clipboard.GetText());}));
    void Duplicate(object s,RoutedEventArgs e)=>Safe(()=>Do(()=>{if(vm.Idle&&vm.Selected.Count>0)vm.Paste(vm.Copy());}));
    void Insert(object s,RoutedEventArgs e){if(!vm.Idle)return;var dialog=new ActionEditor(new(){Kind=ActionKind.Text,Text="Hello from MacroStudio"},MacroDirectory,Labels){Owner=this};if(dialog.ShowDialog()==true)vm.Insert(dialog.Result!);}
    void EditSelected(object s,RoutedEventArgs e){if(vm is null||!vm.Idle||ActionGrid.SelectedItem is not ActionRow row)return;var dialog=new ActionEditor(row.Action,MacroDirectory,Labels){Owner=this};if(dialog.ShowDialog()==true)vm.Edit(row.Action.Id,dialog.Result!);}
    void OnSelectionChanged(object s,SelectionChangedEventArgs e){if(vm is null)return;vm.Selected=ActionGrid.SelectedItems.Cast<ActionRow>().ToList();quickPanel.Visibility=ActionGrid.SelectedItem is ActionRow?Visibility.Visible:Visibility.Collapsed;if(ActionGrid.SelectedItem is ActionRow row){InspectorType.Text=$"Step {row.Number} · {row.Kind}";InspectorDescription.Text=row.Description;InspectorTarget.Text=$"{row.Target}\n{row.Timing}";quickDelay.Text=row.Action.DelayMs.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);quickDuration.Text=row.Action.DurationMs.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);quickText.Text=row.Action.Text;quickComment.Text=row.Comment;}else{InspectorType.Text="";InspectorDescription.Text="";InspectorTarget.Text="";}}
    void CreateInlineInspector() {
        var parent=(StackPanel)InspectorTarget.Parent;
        quickPanel=new(){Margin=new(0,12,0,0),Visibility=Visibility.Collapsed};
        quickPanel.SetBinding(IsEnabledProperty,new System.Windows.Data.Binding("Idle"));
        TextBox Field(string label) {quickPanel.Children.Add(new TextBlock(){Text=label});var box=new TextBox();System.Windows.Automation.AutomationProperties.SetName(box,label);quickPanel.Children.Add(box);return box;}
        quickDelay=Field("Delay before step (ms)");quickDuration=Field("Duration (ms)");quickText=Field("Text entry");quickText.AcceptsReturn=true;quickText.TextWrapping=TextWrapping.Wrap;quickText.MaxHeight=90;quickComment=Field("Step comment");
        var apply=new Button(){Content="Apply properties",Margin=new(0,4,0,0)};quickPanel.Children.Add(apply);
        apply.Click+=(_,_)=>Safe(()=>Do(()=>{if(!vm.Idle||ActionGrid.SelectedItem is not ActionRow row)return;var a=MacroStore.Clone(row.Action);a.DelayMs=double.Parse(quickDelay.Text,System.Globalization.CultureInfo.InvariantCulture);a.DurationMs=double.Parse(quickDuration.Text,System.Globalization.CultureInfo.InvariantCulture);a.Text=quickText.Text;a.Comment=quickComment.Text;MacroStore.Validate(new(){Actions=[a]});vm.Edit(a.Id,a);ActionGrid.SelectedItem=vm.Rows.FirstOrDefault(r=>r.Action.Id==a.Id);vm.Status="Step properties applied";}));
        parent.Children.Insert(parent.Children.IndexOf(InspectorTarget)+1,quickPanel);
    }
    void DragStart(object s,MouseButtonEventArgs e)=>dragOrigin=e.GetPosition(ActionGrid);
    void DragMove(object s,MouseEventArgs e){if(vm is null||!vm.Idle||e.LeftButton!=MouseButtonState.Pressed||vm.Selected.Count==0)return;var p=e.GetPosition(ActionGrid);if(Math.Abs(p.X-dragOrigin.X)>SystemParameters.MinimumHorizontalDragDistance||Math.Abs(p.Y-dragOrigin.Y)>SystemParameters.MinimumVerticalDragDistance){DragDrop.DoDragDrop(ActionGrid,new DataObject("MacroStudio.Rows",true),DragDropEffects.Move);}}
    void DropRows(object s,DragEventArgs e){if(!e.Data.GetDataPresent("MacroStudio.Rows"))return;DependencyObject? source=e.OriginalSource as DependencyObject;while(source!=null&&source is not DataGridRow)source=VisualTreeHelper.GetParent(source);if(source is DataGridRow {Item:ActionRow row})vm.Reorder(row.Action.Id);e.Handled=true;}
    async Task<bool> ConfirmChanges(){if(!vm.Dirty)return true;var result=MessageBox.Show(this,"Save changes to this macro?","Unsaved changes",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);return result==MessageBoxResult.No || result==MessageBoxResult.Yes && await SaveCore(false);}
    void NewFile(object s,RoutedEventArgs e)=>Safe(async()=>{if(vm.Idle&&await ConfirmChanges()){vm.Replace(new());vm.Status="New macro";}});
    void OpenFile(object s,RoutedEventArgs e)=>Safe(async()=>{if(!vm.Idle)return;var dialog=new OpenFileDialog(){Filter="MacroStudio JSON (*.json)|*.json|All files|*.*"};if(dialog.ShowDialog(this)==true&&await ConfirmChanges())await OpenPath(dialog.FileName);});
    async Task OpenPath(string path){var document=await MacroStore.LoadAsync(path);vm.Replace(document,Path.GetFullPath(path));AddRecent(path);vm.Status=$"Opened {Path.GetFileName(path)}";}
    void SaveFile(object s,RoutedEventArgs e)=>Safe(async()=>{if(vm.Idle)await SaveCore(false);});
    void SaveAs(object s,RoutedEventArgs e)=>Safe(async()=>{if(vm.Idle)await SaveCore(true);});
    async Task<bool> SaveCore(bool saveAs){string path=vm.FilePath;if(saveAs||string.IsNullOrWhiteSpace(path)){var dialog=new SaveFileDialog(){Filter="MacroStudio JSON (*.json)|*.json",FileName=vm.Document.Name+".json",DefaultExt=".json"};if(dialog.ShowDialog(this)!=true)return false;path=dialog.FileName;}await MacroStore.SaveAsync(path,vm.Document);vm.MarkSaved(path);AddRecent(path);vm.Status=$"Saved {Path.GetFileName(path)}";return true;}
    void AddRecent(string path){path=Path.GetFullPath(path);preferences.Recent.RemoveAll(p=>p.Equals(path,StringComparison.OrdinalIgnoreCase));preferences.Recent.Insert(0,path);preferences.Recent=preferences.Recent.Take(10).ToList();preferences.Save();RefreshRecent();}
    void RefreshRecent(){RecentMenu.Items.Clear();foreach(var path in preferences.Recent){var item=new MenuItem(){Header=path.Replace("_","__")};item.Click+=(_,_)=>Safe(async()=>{if(vm.Idle&&await ConfirmChanges())await OpenPath(path);});RecentMenu.Items.Add(item);}}
    void UpdateHotkeyHint()=>HotkeyHint.Text=string.Join("   ·   ",preferences.Hotkeys.Select(h=>$"{h.Command} {h}"));
    void ConfigureHotkeys(object s,RoutedEventArgs e){if(!vm.Idle)return;var window=new Window(){Owner=this,Title="Global shortcuts",Width=450,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};var panel=new StackPanel(){Margin=new(24)};window.Content=panel;panel.Children.Add(new TextBlock(){Text="Use key names such as F8 or Ctrl+Shift+R.\nAll four shortcuts must be available and distinct.",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,16)});var boxes=new List<TextBox>();foreach(var h in preferences.Hotkeys){panel.Children.Add(new TextBlock(){Text=h.Command});var box=new TextBox(){Text=h.ToString()};System.Windows.Automation.AutomationProperties.SetName(box,h.Command+" shortcut");boxes.Add(box);panel.Children.Add(box);}var error=new TextBlock(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.IndianRed};panel.Children.Add(error);var save=new Button(){Content="Apply shortcuts"};panel.Children.Add(save);save.Click+=async(_,_)=>{try{var next=boxes.Select((b,i)=>Hotkey.Parse(preferences.Hotkeys[i].Command,b.Text)).ToList();await ConfigureAllHotkeys(next);preferences.Hotkeys=next;preferences.Save();UpdateHotkeyHint();window.Close();}catch(Exception ex){error.Text=ex.Message;}};window.ShowDialog();}
    void ToggleTheme(object s,RoutedEventArgs e){preferences.Dark=!preferences.Dark;SetTheme(preferences.Dark);preferences.Save();}
    void SetTheme(bool dark){vm.Dark=dark;var colors=dark?new[]{"#141A25","#1D2635","#E5EBF6","#ABB8CD","#344259","#739FFF","#344D74"}:new[]{"#F3F5F9","#FFFFFF","#182337","#5A687C","#DCE2EB","#245CD6","#DCE9FF"};string[] names=["Bg","Panel","Ink","Muted","Line","Accent","Selection"];for(int i=0;i<names.Length;i++)Application.Current.Resources[names[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));}
    void OpenLab(object s,RoutedEventArgs e)=>Safe(()=>Do(()=>{var path=Path.Combine(AppContext.BaseDirectory,"Lab","MacroStudio.TestLab.exe");if(!File.Exists(path))throw new FileNotFoundException("Build/package the interaction lab using scripts/build.ps1 first.");Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}));
    void OpenVisualLab(object s,RoutedEventArgs e)=>Safe(()=>Do(()=>{var path=Path.Combine(AppContext.BaseDirectory,"Lab","MacroStudio.TestLab.exe");if(!File.Exists(path))throw new FileNotFoundException("Build/package the interaction lab first.");Process.Start(new ProcessStartInfo(path){ArgumentList={"--visual"},UseShellExecute=true});}));
    void OpenDebugger(object s,RoutedEventArgs e)=>new DebuggerWindow(vm){Owner=this}.Show();
    void ConfigureAi(object s,RoutedEventArgs e)=>new AiConfigurationWindow{Owner=this}.ShowDialog();
    async Task ConfigureAllHotkeys(List<Hotkey>? controls=null){var all=(controls??preferences.Hotkeys).ToList();foreach(var(relative,pref)in library.Preferences.Where(p=>!string.IsNullOrWhiteSpace(p.Value.Hotkey))){var path=library.Resolve(relative);if(File.Exists(path))all.Add(Hotkey.Parse("Library:"+path,pref.Hotkey));}await vm.Hooks.ConfigureAsync(all);}
    void OpenLibrary(object s,RoutedEventArgs e)=>new LibraryWindow(library,vm,async path=>{if(vm.Idle&&await ConfirmChanges())await OpenPath(path);},()=>ConfigureAllHotkeys()){Owner=this}.Show();
    void ExportPackage(object s,RoutedEventArgs e)=>Safe(async()=>{if(!vm.Idle)return;var d=new SaveFileDialog{Filter="MacroStudio portable package|*.macrozip",FileName=vm.Document.Name+".macrozip"};if(d.ShowDialog(this)==true){await PortablePackage.ExportAsync(d.FileName,vm.Document,MacroDirectory);vm.Status="Portable package exported with referenced assets and called macros";}});
    void ImportPackage(object s,RoutedEventArgs e)=>Safe(async()=>{if(!vm.Idle||!await ConfirmChanges())return;var d=new OpenFileDialog{Filter="MacroStudio portable package|*.macrozip"};if(d.ShowDialog(this)==true){var destination=library.Resolve("Imported/"+Path.GetFileNameWithoutExtension(d.FileName)+"-"+Guid.NewGuid().ToString("N")[..6]);await OpenPath(await PortablePackage.ImportAsync(d.FileName,destination));}});
    string MacroDirectory=>string.IsNullOrEmpty(vm.FilePath)?Environment.CurrentDirectory:Path.GetDirectoryName(vm.FilePath)!;
    IEnumerable<string> Labels=>vm.Document.Actions.Where(a=>a.Kind==ActionKind.Label).Select(a=>a.Flow.Label);
    void ToggleBreakpoint(object s,RoutedEventArgs e){if(!vm.Idle)return;vm.Before();foreach(var row in vm.Selected)row.Action.Breakpoint=!row.Action.Breakpoint;vm.Refresh();}
    void Step(object s,RoutedEventArgs e){if(vm.Idle){vm.Engine.StartPaused=true;Safe(()=>vm.PlayAsync());}else vm.Engine.Step();}
    void PreviewPath(object s,RoutedEventArgs e){if(vm.Selected.FirstOrDefault()?.Action is not {} a||a.Kind!=ActionKind.MousePath)return;var w=new Window{Owner=this,Title="Mouse path preview — physical pixels",Width=640,Height=420};var canvas=new System.Windows.Controls.Canvas{Background=Brushes.White,Margin=new(18)};w.Content=canvas;var points=MousePaths.Shape(a,vm.PathShape);if(points.Count>0){double minX=points.Min(p=>p.X),minY=points.Min(p=>p.Y),scale=Math.Min(580/Math.Max(1,points.Max(p=>p.X)-minX),330/Math.Max(1,points.Max(p=>p.Y)-minY));var line=new System.Windows.Shapes.Polyline{Stroke=Brushes.RoyalBlue,StrokeThickness=2,Points=new PointCollection(points.Select(p=>new Point(8+(p.X-minX)*scale,8+(p.Y-minY)*scale)))};canvas.Children.Add(line);}w.Show();}
    void ShowHelp(object s,RoutedEventArgs e)=>MessageBox.Show(this,"1. Press Record, then use another desktop app.\n2. Press the Stop shortcut, select a step and Edit properties.\n3. Save your local JSON, then Play.\n\nUse global shortcuts while another app has focus. The compact controller never activates its window.\nCtrl+N/O/S: files · Ctrl+Z/Y: history\nCtrl+X/C/V/D: cut/copy/paste/duplicate · Delete: remove\nDrag rows to reorder, or use the up/down buttons.\n\nCoordinates use physical pixels. Window-relative actions use offsets from the outer window rectangle. Keep the target app at matching privilege and DPI/layout. Recording captures keyboard input: avoid entering secrets.","MacroStudio quick guide");
    void OnKeyDown(object s,KeyEventArgs e){if(vm is null||!vm.Idle)return;bool editing=e.OriginalSource is TextBox;if(Keyboard.Modifiers==ModifierKeys.Control){if(editing&&e.Key is not (Key.S or Key.O or Key.N))return;switch(e.Key){case Key.S:SaveFile(s,e);break;case Key.O:OpenFile(s,e);break;case Key.N:NewFile(s,e);break;case Key.Z:vm.Undo();break;case Key.Y:vm.Redo();break;case Key.C:Copy(s,e);break;case Key.X:Cut(s,e);break;case Key.V:Paste(s,e);break;case Key.D:Duplicate(s,e);break;default:return;}e.Handled=true;}else if(!editing&&e.Key==Key.Delete){vm.Delete();e.Handled=true;}}
    async void OnClosing(object? s,CancelEventArgs e){if(closeApproved||vm is null)return;e.Cancel=true;if(closingBusy)return;closingBusy=true;try{await Task.Yield();await vm.StopAsync();while(!vm.Idle)await Task.Delay(20);if(!await ConfirmChanges())return;vm.Dispose();controller.Close();closeApproved=true;Close();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Cannot close");}finally{closingBusy=false;}}
}
public sealed class Preferences
{
    static string DirectoryPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MacroStudio");
    public bool Dark {get;set;}public List<string> Recent {get;set;}=[];
    public List<Hotkey> Hotkeys {get;set;}=[new("Record",0,119),new("Play",0,120),new("Pause",0,121),new("Stop",0,27)];
    public static Preferences Load(){try{return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(DirectoryPath,"preferences.json")))??new();}catch{return new();}}
    public void Save(){Directory.CreateDirectory(DirectoryPath);var path=Path.Combine(DirectoryPath,"preferences.json");var temp=path+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(this));File.Move(temp,path,true);}
}
