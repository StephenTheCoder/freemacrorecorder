using System.Windows;
using System.Windows.Controls;
using MacroStudio.Core;
using MacroStudio.Windows;

namespace MacroStudio;
public sealed class AiConfigurationWindow : Window
{
    public AiConfigurationWindow(){
        WindowTheme.Apply(this);var config=AiSettings.Load();Title="Optional AI provider";Width=640;Height=620;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new StackPanel{Margin=new(24)};Content=root;
        root.Children.Add(new TextBlock{Text="AI actions send only their explicitly configured text or screenshot to this endpoint when played. All local features work with AI disabled. Responses are validated typed data; they never become executable macro instructions.",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,16)});
        var enabled=new CheckBox{Content="Enable explicitly invoked AI actions",IsChecked=config.Enabled};root.Children.Add(enabled);
        root.Children.Add(new TextBlock{Text="Provider protocol"});var protocol=new ComboBox{ItemsSource=Enum.GetValues<AiProtocol>(),SelectedItem=config.Protocol};root.Children.Add(protocol);
        TextBox Field(string name,string text){root.Children.Add(new TextBlock{Text=name});var b=new TextBox{Text=text};System.Windows.Automation.AutomationProperties.SetName(b,name);root.Children.Add(b);return b;}
        var endpoint=Field("Full endpoint URL (HTTPS cloud or HTTP loopback)",config.Endpoint);var model=Field("Model identifier installed / available at that endpoint",config.Model);var timeout=Field("Provider timeout (seconds, 1–600)",config.TimeoutSeconds.ToString());
        var useCredential=new CheckBox{Content="Send Bearer credential from Windows Credential Manager",IsChecked=config.UseCredential};root.Children.Add(useCredential);
        root.Children.Add(new TextBlock{Text="New credential (blank keeps the stored value)"});var key=new PasswordBox{Padding=new(7),Margin=new(0,5,0,12)};root.Children.Add(key);
        var buttons=new WrapPanel();root.Children.Add(buttons);var save=new Button{Content="Save configuration"};buttons.Children.Add(save);var remove=new Button{Content="Remove stored credential"};buttons.Children.Add(remove);var error=new TextBlock{TextWrapping=TextWrapping.Wrap};root.Children.Add(error);
        save.Click+=(_,_)=>{try{config.Enabled=enabled.IsChecked==true;config.Protocol=(AiProtocol)protocol.SelectedItem;config.Endpoint=endpoint.Text.Trim();config.Model=model.Text.Trim();config.TimeoutSeconds=int.Parse(timeout.Text);config.UseCredential=useCredential.IsChecked==true;if(config.Enabled)config.Validate();if(!string.IsNullOrEmpty(key.Password))WindowsCredentials.Save(key.Password);AiSettings.Save(config);key.Clear();error.Text="Saved. No provider request was made.";}catch(Exception ex){error.Text=ex.Message;}};
        remove.Click+=(_,_)=>{try{WindowsCredentials.Remove();key.Clear();error.Text="Stored credential removed.";}catch(Exception ex){error.Text=ex.Message;}};
    }
}
