using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using MacroStudio.Core;

namespace MacroStudio.Windows;
public record Hotkey(string Command,uint Modifiers,int Key)
{
    public override string ToString()=> $"{((Modifiers&2)!=0?"Ctrl+":"")}{((Modifiers&1)!=0?"Alt+":"")}{((Modifiers&4)!=0?"Shift+":"")}{((Modifiers&8)!=0?"Win+":"")}{System.Windows.Input.KeyInterop.KeyFromVirtualKey(Key)}";
    public static Hotkey Parse(string command,string text) {
        var parts=text.Split('+',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries); uint mods=0;
        foreach(var p in parts.SkipLast(1)) mods|=p.ToLowerInvariant() switch {"ctrl"=>2u,"alt"=>1u,"shift"=>4u,"win"=>8u,_=>throw new FormatException("Use Ctrl, Alt, Shift or Win modifiers.")};
        if(parts.Length==0 || !Enum.TryParse<System.Windows.Input.Key>(parts[^1],true,out var key)) throw new FormatException("Use a WPF key name, for example F8 or Ctrl+Shift+R.");
        var vk=System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if(vk==0 || vk==123 || vk is 16 or 17 or 18 or 91 or 92 || vk is >=160 and <=165) throw new FormatException("Choose a non-modifier key other than F12 (reserved by Windows).");
        return new(command,mods,vk);
    }
}
public sealed record CapturedInput(double Time,int Message,int X,int Y,int Data,int Vk,int Scan,bool Extended,nint Window,bool Control=false,WindowTarget? Target=null);
public sealed class HookService : IDisposable
{
    readonly Thread thread;
    readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly ConcurrentQueue<Action> commands=new();
    readonly Native.HookProc mouseCallback,keyCallback;
    readonly Native.WinEventProc foregroundCallback;
    readonly HashSet<int> down=[];
    readonly HashSet<int> controlKeys=[];
    readonly Channel<CapturedInput> events=Channel.CreateUnbounded<CapturedInput>(new(){SingleReader=true,SingleWriter=true});
    uint threadId; nint mouseHook,keyHook,foregroundHook;
    volatile bool recording;
    volatile bool disposed;
    double start;
    List<Hotkey> hotkeys=[];
    public event Action<string>? Control;
    public ChannelReader<CapturedInput> Events=>events.Reader;
    public HookService() {
        mouseCallback=Mouse; keyCallback=Keyboard;
        foregroundCallback=(_,_,h,_,_,_,_)=> { if(recording && h!=0 && !WindowCatalog.IsOurs(h)) Queue(new(Time,0,0,0,0,0,0,false,h)); };
        thread=new Thread(Run){IsBackground=true,Name="MacroStudio input hooks"};thread.SetApartmentState(ApartmentState.STA); thread.Start();
        ready.Task.GetAwaiter().GetResult();
    }
    double Time=>Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency-start;
    void Run() {
        threadId=Native.GetCurrentThreadId(); Native.SetThreadDpiAwarenessContext(-4); Native.PeekMessage(out _,0,0,0,0);
        try {
            mouseHook=Native.SetWindowsHookEx(14,mouseCallback,Native.GetModuleHandle(null),0);
            keyHook=Native.SetWindowsHookEx(13,keyCallback,Native.GetModuleHandle(null),0);
            foregroundHook=Native.SetWinEventHook(3,3,0,foregroundCallback,0,0,0);
            if(mouseHook==0 || keyHook==0 || foregroundHook==0) throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not install desktop input hooks.");
            ready.SetResult();
            while(Native.GetMessage(out var msg,0,0,0)>0) {
                if(msg.Msg==0x8001) while(commands.TryDequeue(out var action)) action();
                Native.TranslateMessage(ref msg); Native.DispatchMessage(ref msg);
            }
        } catch(Exception ex) { ready.TrySetException(ex); }
        finally { if(mouseHook!=0) Native.UnhookWindowsHookEx(mouseHook); if(keyHook!=0) Native.UnhookWindowsHookEx(keyHook); if(foregroundHook!=0) Native.UnhookWinEvent(foregroundHook); for(int i=0;i<hotkeys.Count;i++) Native.UnregisterHotKey(0,100+i); events.Writer.TryComplete(); }
    }
    async Task OnThread(Action action) { var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); commands.Enqueue(()=>{try{action();done.SetResult();}catch(Exception ex){done.SetException(ex);}}); if(!Native.PostThreadMessage(threadId,0x8001,0,0)) throw new InvalidOperationException("Input hook thread is unavailable."); await done.Task; }
    public Task ConfigureAsync(List<Hotkey> next)=>OnThread(()=> {
        if(next.Count!=4 || next.Select(k=>(k.Modifiers,k.Key)).Distinct().Count()!=next.Count) throw new InvalidOperationException("Each of the four controls needs a different shortcut.");
        var old=hotkeys.ToList(); for(int i=0;i<old.Count;i++) Native.UnregisterHotKey(0,100+i);
        int registered=0;
        try { for(;registered<next.Count;registered++) if(!Native.RegisterHotKey(0,100+registered,next[registered].Modifiers|0x4000,(uint)next[registered].Key)) throw new InvalidOperationException($"Shortcut {next[registered]} is already in use. Choose another shortcut."); hotkeys=next.ToList(); }
        catch { for(int i=0;i<registered;i++) Native.UnregisterHotKey(0,100+i); for(int i=0;i<old.Count;i++) Native.RegisterHotKey(0,100+i,old[i].Modifiers|0x4000,(uint)old[i].Key); throw; }
    });
    public Task BeginAsync()=>OnThread(()=> { start=Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency; recording=true; var h=Native.GetForegroundWindow(); if(h!=0&&!WindowCatalog.IsOurs(h)) Queue(new(0,0,0,0,0,0,0,false,h)); });
    public Task EndAsync()=>OnThread(()=> {recording=false;Queue(new(Time,-1,0,0,0,0,0,false,0));});
    void Queue(CapturedInput item)=>events.Writer.TryWrite(item);
    nint Mouse(int code,nint message,nint ptr) {
        if(code>=0 && recording) {
            var m=Marshal.PtrToStructure<Native.MouseHook>(ptr);
            if(m.Extra!=(nuint)Native.InjectionTag) {
                var h=Native.GetAncestor(Native.WindowFromPoint(m.Point),2);
                if(!WindowCatalog.IsOurs(h)) Queue(new(Time,(int)message,m.Point.X,m.Point.Y,unchecked((short)(m.Data>>16)),0,0,false,(int)message is 0x201 or 0x204 or 0x207 ? h : Native.GetForegroundWindow()));
            }
        }
        return Native.CallNextHookEx(0,code,message,ptr);
    }
    nint Keyboard(int code,nint message,nint ptr) {
        if(code>=0) {
            var k=Marshal.PtrToStructure<Native.KeyHook>(ptr);
            if(k.Extra!=(nuint)Native.InjectionTag) {
                bool isDown=(int)message is 0x100 or 0x104; int vk=(int)k.Vk; bool fresh=isDown && down.Add(vk);
                if(!isDown) down.Remove(vk);
                uint mods=0; if(down.Overlaps([17,162,163]))mods|=2;if(down.Overlaps([18,164,165]))mods|=1;if(down.Overlaps([16,160,161]))mods|=4;if(down.Overlaps([91,92]))mods|=8;
                var match=isDown?hotkeys.FirstOrDefault(h=>h.Key==vk&&h.Modifiers==mods):null;
                bool control=match!=null||controlKeys.Contains(vk);
                if(match!=null) { controlKeys.Add(vk); if(fresh) { var cmd=match.Command; ThreadPool.QueueUserWorkItem(_=>Control?.Invoke(cmd)); } }
                if(!isDown) controlKeys.Remove(vk);
                var h=Native.GetForegroundWindow();
                if(recording && (control || !WindowCatalog.IsOurs(h))) Queue(new(Time,(int)message,0,0,0,vk,(int)k.Scan,(k.Flags&1)!=0,h,control));
            }
        }
        return Native.CallNextHookEx(0,code,message,ptr);
    }
    public void Dispose() { if(disposed)return;disposed=true;recording=false;Native.PostThreadMessage(threadId,0x12,0,0);thread.Join(3000); }
}
