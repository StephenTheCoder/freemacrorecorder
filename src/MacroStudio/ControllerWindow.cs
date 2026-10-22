using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using MacroStudio.Core;

namespace MacroStudio;
public sealed class ControllerWindow : Window
{
    readonly StudioViewModel vm;
    readonly TextBlock state=new(){Margin=new(8),FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
    public ControllerWindow(StudioViewModel vm,Action<Func<Task>> safe){this.vm=vm;Title="MacroStudio controller";Width=470;Height=65;WindowStyle=WindowStyle.ToolWindow;ResizeMode=ResizeMode.NoResize;Topmost=true;ShowActivated=false;ShowInTaskbar=false;Left=SystemParameters.WorkArea.Right-490;Top=SystemParameters.WorkArea.Bottom-85;
        SetResourceReference(BackgroundProperty,"Panel");SetResourceReference(ForegroundProperty,"Ink");
        var panel=new StackPanel(){Orientation=Orientation.Horizontal,Margin=new(3)};Content=panel;panel.Children.Add(state);var stop=new Button(){Content="■ Stop",Focusable=false};stop.Click+=(_,_)=>safe(vm.StopAsync);panel.Children.Add(stop);var pause=new Button(){Content="Ⅱ Pause / resume",Focusable=false};pause.Click+=(_,_)=>vm.Engine.TogglePause();panel.Children.Add(pause);
        SourceInitialized+=(_,_)=>{var hwnd=new WindowInteropHelper(this).Handle;SetWindowLongPtr(hwnd,-20,GetWindowLongPtr(hwnd,-20)|0x08000000);HwndSource.FromHwnd(hwnd).AddHook((nint h,int message,nint w,nint l,ref bool handled)=>{if(message==0x21){handled=true;return 3;}return 0;});};
    }
    public void UpdateState(){state.Text=vm.Session.State==SessionState.Recording?"● RECORDING":vm.Session.State==SessionState.Paused?"Ⅱ PAUSED":"▶ PLAYBACK";}
}
