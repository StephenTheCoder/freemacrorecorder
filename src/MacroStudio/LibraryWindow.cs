using System.IO;
using System.Windows;
using System.Windows.Controls;
using MacroStudio.Core;
using Microsoft.Win32;

namespace MacroStudio;
public sealed class LibraryWindow : Window
{
    public LibraryWindow(LocalLibrary library,StudioViewModel vm,Func<string,Task> open,Func<Task> hotkeys){
        WindowTheme.Apply(this);Title="Local macro library";Width=900;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new(20)};Content=root;
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);top.Children.Add(new TextBlock{Text=library.Root,TextWrapping=TextWrapping.Wrap});
        var search=new TextBox{ToolTip="Search macro names and folder paths"};System.Windows.Automation.AutomationProperties.SetName(search,"Search macro library");top.Children.Add(search);var favorites=new CheckBox{Content="Favorites only"};top.Children.Add(favorites);
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);var error=new TextBlock{TextWrapping=TextWrapping.Wrap};bottom.Children.Add(error);
        var list=new ListBox{DisplayMemberPath="Display"};root.Children.Add(list);
        void Refresh()=>list.ItemsSource=library.Search(search.Text,favorites.IsChecked==true);search.TextChanged+=(_,_)=>Refresh();favorites.Click+=(_,_)=>Refresh();
        var row=new WrapPanel();bottom.Children.Add(row);
        void Button(string label,Func<Task> action){var b=new Button{Content=label};row.Children.Add(b);b.Click+=async(_,_)=>{try{if(!vm.Idle)throw new InvalidOperationException("Stop the session before changing the library.");b.IsEnabled=false;await action();Refresh();}catch(Exception ex){error.Text=ex.Message;}finally{b.IsEnabled=true;}};}
        LibraryEntry Selected()=>list.SelectedItem as LibraryEntry??throw new InvalidOperationException("Select a macro first.");
        Button("Open",async()=>{await open(Selected().Path);});Button("Run",async()=>{await vm.PlayFileAsync(Selected().Path);});Button("Favorite / unfavorite",async()=>{var e=Selected();library.SavePreference(e.RelativePath,!e.Favorite,e.Hotkey);await Task.CompletedTask;});
        Button("Export portable…",async()=>{var e=Selected();var d=new SaveFileDialog{Filter="MacroStudio package|*.macrozip",FileName=Path.GetFileNameWithoutExtension(e.Path)+".macrozip"};if(d.ShowDialog(this)==true)await PortablePackage.ExportAsync(d.FileName,await MacroStore.LoadAsync(e.Path),Path.GetDirectoryName(e.Path)!);});
        Button("Import portable…",async()=>{var d=new OpenFileDialog{Filter="MacroStudio package|*.macrozip"};if(d.ShowDialog(this)==true){var dir=library.Resolve(Path.GetFileNameWithoutExtension(d.FileName)+"-"+Guid.NewGuid().ToString("N")[..6]);await PortablePackage.ImportAsync(d.FileName,dir);}});
        var path=new TextBox{Text="My macros",ToolTip="Relative folder path inside the library"};System.Windows.Automation.AutomationProperties.SetName(path,"Library folder path");bottom.Children.Add(path);
        Button("Create folder",async()=>{library.CreateFolder(path.Text);await Task.CompletedTask;});
        Button("Add current macro",async()=>{var relative=Path.Combine(path.Text,Guid.NewGuid().ToString("N")[..8]);var package=Path.Combine(Path.GetTempPath(),"MacroStudio-"+Guid.NewGuid()+".macrozip");try{await PortablePackage.ExportAsync(package,vm.Document,string.IsNullOrEmpty(vm.FilePath)?Environment.CurrentDirectory:Path.GetDirectoryName(vm.FilePath)!);await PortablePackage.ImportAsync(package,library.Resolve(relative));}finally{if(File.Exists(package))File.Delete(package);}});
        var key=new TextBox{ToolTip="Per-macro global shortcut; blank removes it (e.g. Ctrl+Shift+F6)"};System.Windows.Automation.AutomationProperties.SetName(key,"Per-macro hotkey");bottom.Children.Add(key);list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is LibraryEntry e)key.Text=e.Hotkey;};
        Button("Assign hotkey",async()=>{var e=Selected();var old=library.Preferences.GetValueOrDefault(e.RelativePath);library.Preferences[e.RelativePath]=new(){Favorite=e.Favorite,Hotkey=key.Text};try{await hotkeys();library.SavePreference(e.RelativePath,e.Favorite,key.Text);}catch{if(old is null)library.Preferences.Remove(e.RelativePath);else library.Preferences[e.RelativePath]=old;throw;}});
        Refresh();
    }
}
