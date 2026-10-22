using System.Collections.Concurrent;

namespace MacroStudio.Core;
public sealed record ExecutionLog(DateTimeOffset Time,string Level,Guid Action,string Message);
public sealed class PlaybackEngine(IInputSink sink,SessionMachine session,IAutomationHost? automation=null)
{
    readonly object controlGate=new();
    CancellationTokenSource? cancellation;
    PlaybackClock? clock;
    bool pauseNext;
    readonly ConcurrentQueue<ExecutionLog> logs=new();
    public IReadOnlyList<ExecutionLog> Logs=>logs.ToArray();
    public event Action<Guid,int>? Progress;
    public event Action? VariablesChanged;
    public AutomationContext? Context {get;private set;}
    public string BaseDirectory {get;set;}=Environment.CurrentDirectory;
    public string? RootFile {get;set;}
    public bool StartPaused {get;set;}
    public bool RespectBreakpoints {get;set;}=true;
    void Log(string level,MacroAction a,string text){logs.Enqueue(new(DateTimeOffset.Now,level,a.Id,text));while(logs.Count>10000)logs.TryDequeue(out _);}
    public void Stop(){lock(controlGate){if(session.State is SessionState.Playing or SessionState.Paused)session.Transition(SessionState.Stopping);cancellation?.Cancel();}}
    public void TogglePause(){lock(controlGate){if(session.State==SessionState.Playing){clock?.Pause();session.Transition(SessionState.Paused);}else if(session.State==SessionState.Paused){pauseNext=false;clock?.Resume();session.Transition(SessionState.Playing);}}}
    public void Step(){lock(controlGate){if(session.State!=SessionState.Paused)return;pauseNext=true;clock?.Resume();session.Transition(SessionState.Playing);}}
    void Pause(){lock(controlGate){if(session.State==SessionState.Playing){clock?.Pause();session.Transition(SessionState.Paused);}}}
    public async Task PlayAsync(IEnumerable<MacroAction> source,PlaybackOptions options,CancellationToken token=default,double startDelayMs=0){
        var actions=source.Select(MacroStore.Clone).ToList();MacroStore.Validate(new(){Actions=actions,Playback=options});
        if(!actions.Any(a=>a.Enabled))throw new InvalidOperationException("There are no enabled actions to play.");
        // Complete file/asset/label/recursion validation before state changes or any injected input.
        var program=await MacroProgram.BuildAsync(actions,BaseDirectory,RootFile,token);
        if(automation!=null)await automation.ValidateAsync(program,token);
        using var cts=CancellationTokenSource.CreateLinkedTokenSource(token);
        lock(controlGate){if(session.State!=SessionState.Idle)throw new InvalidOperationException("Stop the current session first.");cancellation=cts;clock=new();pauseNext=StartPaused;StartPaused=false;while(logs.TryDequeue(out _)){}if(!session.Transition(SessionState.Playing)){cancellation=null;clock=null;throw new InvalidOperationException("Stop the current session first.");}}
        try{
            var timeline=clock;
            var context=new AutomationContext{BaseDirectory=BaseDirectory,Checkpoint=t=>timeline.WaitUntilAsync(timeline.Elapsed,t)};Context=context;context.Variables.Changed+=()=>VariablesChanged?.Invoke();VariablesChanged?.Invoke();
            double deadline=Math.Max(0,startDelayMs);
            async Task Run(MacroProgram p,int repetition){
                var previousFolder=context.BaseDirectory;context.BaseDirectory=p.Directory;
                var remaining=new Dictionary<int,int>();
                try{for(int pc=0;pc<p.Actions.Count;pc++){
                    cts.Token.ThrowIfCancellationRequested();var original=p.Actions[pc];
                    Progress?.Invoke(original.Id,repetition);
                    if(pauseNext||RespectBreakpoints&&original.Breakpoint){pauseNext=false;Pause();Log("Pause",original,"Paused before action");}
                    await context.Checkpoint(cts.Token);
                    var a=context.Variables.Resolve(original);
                    deadline+=PlaybackClock.Scale(a.DelayMs,options.Speed);await timeline.WaitUntilAsync(deadline,cts.Token);
                    Log("Run",a,a.Kind.ToString());
                    int? jump=null;
                    try{
                        if(a.Kind==ActionKind.MousePath){foreach(var point in MousePaths.Shape(a,options.PathShape)){await timeline.WaitUntilAsync(deadline+PlaybackClock.Scale(point.OffsetMs,options.Speed),cts.Token);if(!options.SkipMousePaths)sink.Move(point.X,point.Y,a);}deadline+=PlaybackClock.Scale(a.DurationMs,options.Speed);}
                        else if(a.Kind==ActionKind.Delay)deadline+=PlaybackClock.Scale(a.DurationMs,options.Speed);
                        else switch(a.Kind){
                            case ActionKind.MouseButton:sink.Move(a.X,a.Y,a);sink.Button(a.Button,a.IsDown);break;
                            case ActionKind.Scroll:sink.Move(a.X,a.Y,a);sink.Scroll(a.WheelDelta,a.Horizontal);break;
                            case ActionKind.Key:sink.Key(a.VirtualKey,a.ScanCode,a.Extended,a.IsDown);break;
                            case ActionKind.KeyChord:foreach(var key in a.Keys){cts.Token.ThrowIfCancellationRequested();sink.Key(key,0,false,true);}foreach(var key in a.Keys.AsEnumerable().Reverse())sink.Key(key,0,false,false);break;
                            case ActionKind.Text:foreach(var c in a.Text){cts.Token.ThrowIfCancellationRequested();await context.Checkpoint(cts.Token);sink.Unicode(c,true);sink.Unicode(c,false);}break;
                            case ActionKind.FocusWindow:sink.Focus(a.Target!,options.RestoreWindows);break;
                            case ActionKind.Label:break;
                            case ActionKind.Jump:jump=p.Labels[a.Flow.Destination];break;
                            case ActionKind.Branch:var label=DataOperations.Compare(a.Data.Value,a.Data.Operation,a.Data.Operand)?a.Flow.Destination:a.Flow.Otherwise;if(!string.IsNullOrEmpty(label))jump=p.Labels[label];break;
                            case ActionKind.RepeatStart:remaining.TryAdd(pc,a.Flow.Count);break;
                            case ActionKind.RepeatEnd:int begin=p.Repeats[pc];if(--remaining[begin]>0)jump=begin+1;else remaining.Remove(begin);break;
                            case ActionKind.CallMacro:await Run(p.Calls[pc],repetition);deadline=Math.Max(deadline,timeline.Elapsed);break;
                            default:
                                if(DataOperations.Execute(a,context))break;
                                if(automation is null)throw new InvalidOperationException($"No automation host is configured for {a.Kind}.");
                                for(int attempt=0;;attempt++){
                                    try{await automation.ExecuteAsync(a,context,cts.Token);break;}
                                    catch(TimeoutException ex){Log("Timeout",a,ex.Message);if(a.Flow.Timeout==TimeoutPolicy.Retry&&attempt<a.Flow.Retries){await context.Checkpoint(cts.Token);Log("Retry",a,$"Retry {attempt+1}/{a.Flow.Retries}");continue;}if(a.Flow.Timeout==TimeoutPolicy.Jump){jump=p.Labels[a.Flow.Destination];break;}throw;}
                                }
                                deadline=Math.Max(deadline,timeline.Elapsed);break;
                        }
                        await timeline.WaitUntilAsync(deadline,cts.Token);
                    }catch(Exception ex){Log(ex is OperationCanceledException?"Stopped":"Error",a,ex.Message);throw;}
                    if(jump.HasValue)pc=jump.Value-1;
                    // Includes labels, jumps and zero-duration repeats; every instruction is interruptible.
                    await Task.Delay(1,cts.Token);
                }}finally{context.BaseDirectory=previousFolder;}
            }
            for(int repetition=1;options.UntilStopped||repetition<=options.Repetitions;repetition++){await Run(program,repetition);sink.ReleaseAll();await Task.Delay(1,cts.Token);deadline=Math.Max(deadline,timeline.Elapsed);}
        }finally{try{sink.ReleaseAll();}finally{lock(controlGate){cancellation=null;clock=null;if(session.State!=SessionState.Stopping)session.Transition(SessionState.Stopping);session.Transition(SessionState.Idle);}}}
    }
}
