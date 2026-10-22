using MacroStudio.Core;

namespace MacroStudio.Windows;
public sealed class RecordingService : IDisposable
{
    readonly HookService hooks;
    readonly SessionMachine session;
    readonly Task worker;
    readonly List<CapturedInput> raw=[];
    TaskCompletionSource<List<MacroAction>>? completion;
    CoordinateMode mode;
    public event Action<int>? CountChanged;
    public RecordingService(HookService hooks,SessionMachine session) { this.hooks=hooks;this.session=session;worker=Task.Run(Consume); }
    public async Task StartAsync(CoordinateMode coordinateMode) {
        if(!session.Transition(SessionState.Recording)) throw new InvalidOperationException("Stop the current session first.");
        mode=coordinateMode;completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        try { await hooks.BeginAsync(); } catch { session.Transition(SessionState.Stopping);session.Transition(SessionState.Idle);throw; }
    }
    public async Task<List<MacroAction>> StopAsync() {
        if(!session.Transition(SessionState.Stopping) || completion is null) return [];
        try { await hooks.EndAsync(); return await completion.Task; }
        finally { session.Transition(SessionState.Idle); }
    }
    async Task Consume() {
        await foreach(var item in hooks.Events.ReadAllAsync()) {
            if(item.Message==-1) { try { completion?.TrySetResult(Assemble(raw,mode)); } catch(Exception ex) {completion?.TrySetException(ex);} raw.Clear(); }
            else { raw.Add(item with {Target=item.Window==0?null:WindowCatalog.Describe(item.Window)}); if(raw.Count%40==0)CountChanged?.Invoke(raw.Count); }
        }
    }
    public static List<MacroAction> Assemble(IReadOnlyList<CapturedInput> input,CoordinateMode mode) {
        // Remove the modifier edges belonging solely to control chords, including a stop chord whose up arrives after recording ends.
        var remove=new HashSet<int>();
        for(int i=0;i<input.Count;i++) if(input[i].Control) {
            remove.Add(i);
            if(input[i].Message is not (0x100 or 0x104))continue;
            foreach(int modifier in new[]{16,17,18,91,92,160,161,162,163,164,165}) {
                int begin=-1;
                for(int j=i-1;j>=0;j--)if(input[j].Vk==modifier) {if(input[j].Message is 0x100 or 0x104)begin=j;break;}
                if(begin<0)continue;
                int end=input.Count;
                for(int j=i+1;j<input.Count;j++) if(input[j].Vk==modifier && input[j].Message is 0x101 or 0x105) {end=j;break;}
                bool used=false;
                for(int j=begin+1;j<end;j++) if(input[j].Message is not (0 or 0x200) && !input[j].Control && !(input[j].Vk is 16 or 17 or 18 or 91 or 92 or >=160 and <=165))used=true;
                if(used)continue;
                remove.Add(begin);
                for(int j=begin+1;j<=Math.Min(end,input.Count-1);j++) if(input[j].Vk==modifier) remove.Add(j);
            }
        }
        var result=new List<MacroAction>(); MacroAction? path=null; double last=0,pathStart=0; nint lastWindow=0; WindowTarget? target=null;
        void Flush() { if(path!=null) { result.Add(path);last=pathStart+path.DurationMs;path=null; } }
        for(int i=0;i<input.Count;i++) {
            var r=input[i];if(remove.Contains(i))continue;
            if((r.Window!=lastWindow || r.Target is not null && r.Target!=target) && r.Window!=0 && !WindowCatalog.IsOurs(r.Window)) {
                Flush(); target=r.Target??WindowCatalog.Describe(r.Window);lastWindow=r.Window;
                if(target!=null) {result.Add(new(){Kind=ActionKind.FocusWindow,Target=target,DelayMs=Math.Max(0,r.Time-last)});last=r.Time;}
            }
            if(r.Message==0)continue;
            int x=r.X,y=r.Y; if(mode==CoordinateMode.Window && target!=null){x-=target.Left;y-=target.Top;}
            if(r.Message==0x200) {
                if(path==null) {pathStart=r.Time;path=new(){Kind=ActionKind.MousePath,DelayMs=Math.Max(0,r.Time-last),Target=target,Coordinates=target==null?CoordinateMode.Desktop:mode};}
                path.Path.Add(new(x,y,Math.Max(0,r.Time-pathStart))); path.DurationMs=Math.Max(0,r.Time-pathStart);continue;
            }
            Flush(); var a=new MacroAction(){DelayMs=Math.Max(0,r.Time-last),Target=target,Coordinates=target==null?CoordinateMode.Desktop:mode,X=x,Y=y};
            if(r.Vk!=0) {a.Kind=ActionKind.Key;a.VirtualKey=r.Vk;a.ScanCode=r.Scan;a.Extended=r.Extended;a.IsDown=r.Message is 0x100 or 0x104;}
            else if(r.Message is 0x20A or 0x20E) {a.Kind=ActionKind.Scroll;a.WheelDelta=r.Data;a.Horizontal=r.Message==0x20E;}
            else if(r.Message is 0x201 or 0x202 or 0x204 or 0x205 or 0x207 or 0x208) {a.Kind=ActionKind.MouseButton;a.Button=r.Message is 0x201 or 0x202?MouseButton.Left:r.Message is 0x204 or 0x205?MouseButton.Right:MouseButton.Middle;a.IsDown=r.Message is 0x201 or 0x204 or 0x207;}
            else continue;
            result.Add(a);last=r.Time;
        }
        Flush();return result;
    }
    public void Dispose() { /* HookService owns and completes the channel. */ }
}
