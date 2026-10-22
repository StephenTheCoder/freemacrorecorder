using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MacroStudio.Core;

namespace MacroStudio.Windows;
public static class WindowCatalog
{
    public static bool IsOurs(nint hwnd) { Native.GetWindowThreadProcessId(hwnd,out var pid); return pid == Environment.ProcessId; }
    public static WindowTarget? Describe(nint hwnd) {
        var previousDpi=Native.SetThreadDpiAwarenessContext(-4);
        try {
        if (hwnd == 0 || !Native.GetWindowRect(hwnd,out var r)) return null;
        Native.GetWindowThreadProcessId(hwnd,out var pid);
        try {
            using var process = Process.GetProcessById((int)pid); var title=new StringBuilder(2048); Native.GetWindowText(hwnd,title,title.Capacity);
            return new(process.ProcessName,title.ToString(),r.Left,r.Top,Math.Max(1,r.Right-r.Left),Math.Max(1,r.Bottom-r.Top));
        } catch { return null; }
        } finally { Native.SetThreadDpiAwarenessContext(previousDpi); }
    }
    public static nint Find(WindowTarget target) {
        var matches = new List<(nint Handle,WindowTarget Target)>();
        Native.EnumWindows((h,_)=> { if (Native.IsWindowVisible(h) && Describe(h) is { } w && w.ProcessName.Equals(target.ProcessName,StringComparison.OrdinalIgnoreCase)) matches.Add((h,w)); return true; },0);
        var exact=matches.Where(m=>m.Target.Title.Equals(target.Title,StringComparison.OrdinalIgnoreCase)).ToList();
        if(exact.Count == 1) return exact[0].Handle;
        var partial=matches.Where(m=>target.Title.Length>0 && m.Target.Title.Contains(target.Title,StringComparison.OrdinalIgnoreCase)).ToList();
        if(partial.Count == 1) return partial[0].Handle;
        if(matches.Count == 1) return matches[0].Handle;
        throw new InvalidOperationException(matches.Count==0 ? $"Target window not found: {target.ProcessName} · {target.Title}. Open it before playback." : $"Multiple windows match {target.ProcessName}. Use a unique title in the action target.");
    }
}
public sealed class WindowsInput : IInputSink
{
    readonly HashSet<(int Vk,int Scan,bool Extended)> keys=[];
    readonly HashSet<MouseButton> buttons=[];
    readonly HashSet<char> unicode=[];
    Guid pointerAction;
    nint pointerWindow;
    public static int Normalize(int coordinate,int origin,int size) => (int)Math.Clamp(Math.Round((coordinate-(double)origin)*65535/Math.Max(1,size-1)),0,65535);
    void Send(Native.Input input) {
        if(Native.SendInput(1,[input],Marshal.SizeOf<Native.Input>())!=1)
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows rejected input injection. The target may be elevated or on a secure desktop. Run both apps at the same privilege level.");
    }
    public void Move(int x,int y,MacroAction action) {
        var old=Native.SetThreadDpiAwarenessContext(-4);
        try {
            if(action.Coordinates==CoordinateMode.Window) {
                // Resolve once per action; path samples reuse that live handle and read current physical bounds.
                if(pointerAction!=action.Id||pointerWindow==0){pointerWindow=WindowCatalog.Find(action.Target!);pointerAction=action.Id;}
                if(!Native.GetWindowRect(pointerWindow,out var r)){pointerWindow=0;throw new InvalidOperationException("Cannot read target window bounds.");}x+=r.Left;y+=r.Top;
            }
            Send(new() { Type=0,Data=new(){Mouse=new(){X=Normalize(x,Native.GetSystemMetrics(76),Native.GetSystemMetrics(78)),Y=Normalize(y,Native.GetSystemMetrics(77),Native.GetSystemMetrics(79)),Flags=0x8000|0x4000|1,Extra=(nuint)Native.InjectionTag}} });
        } finally { Native.SetThreadDpiAwarenessContext(old); }
    }
    public void Button(MouseButton button,bool down) {
        if(down) buttons.Add(button);
        var flag=button switch { MouseButton.Left=>down?2u:4u, MouseButton.Right=>down?8u:16u,_=>down?32u:64u };
        Send(new(){Data=new(){Mouse=new(){Flags=flag,Extra=(nuint)Native.InjectionTag}}});
        if(!down) buttons.Remove(button);
    }
    public void Scroll(int delta,bool horizontal) => Send(new(){Data=new(){Mouse=new(){Flags=horizontal?0x1000u:0x800u,Data=unchecked((uint)delta),Extra=(nuint)Native.InjectionTag}}});
    public void Key(int vk,int scan,bool extended,bool down) {
        var key=(vk,scan,extended); if(down) keys.Add(key);
        Send(new(){Type=1,Data=new(){Key=new(){Vk=(ushort)(scan==0?vk:0),Scan=(ushort)scan,Flags=(scan!=0?8u:0)|(extended?1u:0)|(down?0:2u),Extra=(nuint)Native.InjectionTag}}});
        if(!down) keys.Remove(key);
    }
    public void Unicode(char c,bool down) {
        if(down) unicode.Add(c);
        Send(new(){Type=1,Data=new(){Key=new(){Scan=c,Flags=4u|(down?0:2u),Extra=(nuint)Native.InjectionTag}}}); if(!down) unicode.Remove(c);
    }
    public void Focus(WindowTarget target,bool restore) {
        var hwnd=WindowCatalog.Find(target); Native.ShowWindow(hwnd,9);
        if(restore && !Native.SetWindowPos(hwnd,0,target.Left,target.Top,target.Width,target.Height,0x14)) throw new Win32Exception("Could not restore the target window bounds.");
        if(!Native.SetForegroundWindow(hwnd) && Native.GetForegroundWindow()!=hwnd) throw new InvalidOperationException($"Windows prevented focus of {target.Title}. Activate the target and try again.");
    }
    public void ReleaseAll() {
        pointerWindow=0;
        var failures=new List<Exception>();
        foreach(var k in keys.ToArray()) try { Key(k.Vk,k.Scan,k.Extended,false); } catch(Exception ex) { failures.Add(ex); }
        foreach(var b in buttons.ToArray()) try { Button(b,false); } catch(Exception ex) { failures.Add(ex); }
        foreach(var c in unicode.ToArray()) try { Unicode(c,false); } catch(Exception ex) { failures.Add(ex); }
        if(failures.Count>0) throw new AggregateException("Windows could not release all held inputs. Check the target privilege level.",failures);
    }
}
