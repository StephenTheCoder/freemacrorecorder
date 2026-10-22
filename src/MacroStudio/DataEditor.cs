using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MacroStudio.Core;

namespace MacroStudio;
public sealed class DataEditor : Window
{
    public DataSettings Result {get;private set;}
    public Dictionary<string,string> Bindings {get;private set;}
    public DataEditor(MacroAction action){
        WindowTheme.Apply(this);Result=MacroStore.Clone(action).Data;Bindings=new(action.Bindings);Title="Variables, OCR and data";Width=620;Height=760;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new(22)};Content=root;var apply=new Button{Content="Apply data settings"};DockPanel.SetDock(apply,Dock.Bottom);root.Children.Add(apply);var panel=new StackPanel();root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        panel.Children.Add(new TextBlock{Text="Use ${name}, ${point.x}, ${point.y} in text, paths and operands. Screenshot/OCR region is set under Visual target / wait settings.",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)});
        TextBox Field(string name,string value,bool multiline=false){panel.Children.Add(new TextBlock{Text=name});var box=new TextBox{Text=value,AcceptsReturn=multiline,MinHeight=multiline?80:0,TextWrapping=TextWrapping.Wrap};System.Windows.Automation.AutomationProperties.SetName(box,name);panel.Children.Add(box);return box;}
        var variable=Field("Output variable (blank allowed only for screenshot-to-file)",Result.Variable);panel.Children.Add(new TextBlock{Text="Value type (SetVariable / AI output)"});var type=new ComboBox{ItemsSource=Enum.GetValues<VariableType>(),SelectedItem=Result.Type};panel.Children.Add(type);
        var value=Field("Value / left operand / OCR search text",Result.Value,true);var operand=Field("Right operand",Result.Operand);var operation=Field("Operation (+ - * / % or == != contains > < >= <=)",Result.Operation);var file=Field("Text / CSV / screenshot / AI image file path (relative to macro)",Result.File);var append=new CheckBox{Content="Append text when writing",IsChecked=Result.Append};panel.Children.Add(append);var row=Field("CSV row (zero based)",Result.Row.ToString());var column=Field("CSV column (zero based)",Result.Column.ToString());var language=Field("Installed Windows OCR language",Result.Language);
        var bindings=Field("Numeric property bindings JSON (e.g. {\"x\":\"${point.x}\",\"y\":\"${point.y}\"})",JsonSerializer.Serialize(Bindings,MacroStore.Json),true);
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap};panel.Children.Add(error);apply.Click+=(_,_)=>{try{Result.Variable=variable.Text;Result.Type=(VariableType)type.SelectedItem;Result.Value=value.Text;Result.Operand=operand.Text;Result.Operation=operation.Text;Result.File=file.Text;Result.Append=append.IsChecked==true;Result.Row=int.Parse(row.Text);Result.Column=int.Parse(column.Text);Result.Language=language.Text;Bindings=JsonSerializer.Deserialize<Dictionary<string,string>>(bindings.Text,MacroStore.Json)??[];if(Bindings.Keys.Any(k=>!VariableStore.AllowedBindings.Contains(k)))throw new InvalidOperationException("Unsupported property binding. See documentation for supported properties.");DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}};
    }
}
