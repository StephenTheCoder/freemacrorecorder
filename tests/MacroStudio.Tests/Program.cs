using System.Diagnostics;
using System.IO;
using MacroStudio.Core;
using MacroStudio.Windows;

var tests=new List<(string,Func<Task>)>();
void Add(string name,Action run)=>tests.Add((name,()=>{run();return Task.CompletedTask;}));
void Assert(bool ok,string message="Assertion failed"){if(!ok)throw new Exception(message);}
void Reject(Action run){try{run();}catch(InvalidDataException){return;}throw new Exception("Expected invalid document to be rejected");}
Add("playback selection: one, inclusive range, from selection and snapshot isolation",()=>{
    var d=new MacroDocument(){Actions=Enumerable.Range(0,5).Select(i=>new MacroAction(){Kind=ActionKind.Text,Text=i.ToString()}).ToList(),Playback=new(){UntilStopped=true,Repetitions=9}};
    var ids=new[]{d.Actions[1].Id,d.Actions[3].Id};
    var one=PlaybackSelection.Create(d,ids,"one");Assert(one.Actions.Count==1&&one.Actions[0].Text=="1"&&!one.Options.UntilStopped&&one.Options.Repetitions==1);
    var range=PlaybackSelection.Create(d,ids,"range");Assert(string.Join("",range.Actions.Select(a=>a.Text))=="123");
    var from=PlaybackSelection.Create(d,ids,"from");Assert(string.Join("",from.Actions.Select(a=>a.Text))=="1234");
    range.Actions[0].Text="changed";range.Options.Repetitions=1;Assert(d.Actions[1].Text=="1"&&d.Playback.Repetitions==9);
    try{PlaybackSelection.Create(d,[],"range");throw new Exception("Empty selection accepted");}catch(InvalidOperationException){}
});
Add("serialization: Unicode, negative coordinates, all action types and order",()=>{
    var d=new MacroDocument(){Actions=Enum.GetValues<ActionKind>().Select((kind,i)=>new MacroAction(){Kind=kind,X=-1600,Y=-200,Text="λ 🪟 日本語",VirtualKey=65,Keys=[17,65],DurationMs=25,Path=[new(-12,5,0),new(12,20,25)],Target=new("notepad","Notes",-1600,0,800,600),Comment=i.ToString()}).ToList()};
    var actual=MacroStore.Parse(MacroStore.Serialize(d));Assert(actual.Actions.Select(a=>a.Kind).SequenceEqual(d.Actions.Select(a=>a.Kind)));Assert(actual.Actions[0].X==-1600&&actual.Actions[0].Text.Contains("🪟"));
});
Add("validation: version, malformed JSON, duplicate IDs, invalid paths and timing",()=>{
    Reject(()=>MacroStore.Parse("{"));Reject(()=>MacroStore.Parse("{\"formatVersion\":99}"));Reject(()=>MacroStore.Parse("{\"unknown\":true}"));
    var a=new MacroAction();Reject(()=>MacroStore.Serialize(new(){Actions=[a,a]}));
    Reject(()=>MacroStore.Serialize(new(){Actions=[new(){DelayMs=-1}]}));
    Reject(()=>MacroStore.Serialize(new(){Actions=[new(){DurationMs=10,Path=[new(0,0,11)]}]}));
    Reject(()=>MacroStore.Serialize(new(){Actions=[new(){DurationMs=10,Path=[new(0,0,9),new(0,0,2)]}]}));
    Reject(()=>MacroStore.Serialize(new(){Actions=[new(){Kind=ActionKind.FocusWindow}]}));
});
tests.Add(("atomic save and reopen",async()=>{var dir=Path.Combine(Path.GetTempPath(),"MacroStudio-tests-"+Guid.NewGuid());Directory.CreateDirectory(dir);try{var path=Path.Combine(dir,"macro.json");await MacroStore.SaveAsync(path,new(){Name="First"});await MacroStore.SaveAsync(path,new(){Name="Edited"});Assert((await MacroStore.LoadAsync(path)).Name=="Edited");Assert(Directory.GetFiles(dir).Length==1);}finally{Directory.Delete(dir,true);}}));
Add("state transitions prevent recording/playback conflicts",()=>{var s=new SessionMachine();Assert(s.Transition(SessionState.Recording));Assert(!s.Transition(SessionState.Playing));Assert(!s.Transition(SessionState.Paused));Assert(s.Transition(SessionState.Stopping));Assert(!s.Transition(SessionState.Recording));Assert(s.Transition(SessionState.Idle));Assert(s.Transition(SessionState.Playing));Assert(s.Transition(SessionState.Paused));Assert(s.Transition(SessionState.Playing));});
Add("timing speed calculations and negative virtual desktop normalization",()=>{Assert(PlaybackClock.Scale(1000,2)==500);Assert(PlaybackClock.Scale(100,.5)==200);Assert(WindowsInput.Normalize(-1920,-1920,3840)==0);Assert(WindowsInput.Normalize(1919,-1920,3840)==65535);Assert(WindowsInput.Normalize(0,-1920,3840)>32767);});
Add("recording path grouping, drag edge order and timing",()=>{var rows=RecordingService.Assemble([new(10,0x201,-5,2,0,0,0,false,0),new(20,0x200,-4,2,0,0,0,false,0),new(40,0x200,20,3,0,0,0,false,0),new(50,0x202,20,3,0,0,0,false,0)],CoordinateMode.Desktop);Assert(rows.Count==3);Assert(rows[0].IsDown&&!rows[2].IsDown);Assert(rows[1].Path.Count==2&&rows[1].DurationMs==20&&rows[1].DelayMs==10&&rows[2].DelayMs==10);});
Add("control shortcut and modifier exclusion",()=>{var rows=RecordingService.Assemble([new(1,0x100,0,0,0,162,29,false,0),new(2,0x100,0,0,0,119,66,false,0,true),new(3,0x101,0,0,0,119,66,false,0,true),new(4,0x101,0,0,0,162,29,false,0)],CoordinateMode.Desktop);Assert(rows.Count==0);});
Add("control chords preserve a modifier used by a following ordinary key",()=>{var rows=RecordingService.Assemble([new(1,0x100,0,0,0,162,29,false,0),new(2,0x100,0,0,0,119,66,false,0,true),new(3,0x101,0,0,0,119,66,false,0,true),new(4,0x100,0,0,0,65,30,false,0),new(5,0x101,0,0,0,65,30,false,0),new(6,0x101,0,0,0,162,29,false,0)],CoordinateMode.Desktop);Assert(rows.Select(a=>a.VirtualKey).SequenceEqual(new[]{162,65,65,162}));});
Add("movement around a control chord does not retain its modifiers",()=>{var rows=RecordingService.Assemble([new(1,0x100,0,0,0,162,29,false,0),new(2,0x200,4,5,0,0,0,false,0),new(3,0x100,0,0,0,119,66,false,0,true),new(4,0x101,0,0,0,162,29,false,0)],CoordinateMode.Desktop);Assert(rows.Count==1&&rows[0].Kind==ActionKind.MousePath);});
tests.Add(("stop at the Playing transition cannot miss cancellation",async()=>{var sink=new FakeSink();var s=new SessionMachine();var e=new PlaybackEngine(sink,s);s.Changed+=state=>{if(state==SessionState.Playing)e.Stop();};var sw=Stopwatch.StartNew();try{await e.PlayAsync([new(){Kind=ActionKind.Delay,DurationMs=60000}],new());throw new Exception("Expected cancellation");}catch(OperationCanceledException){}Assert(sw.ElapsedMilliseconds<500&&s.State==SessionState.Idle&&sink.Releases==1);}));
tests.Add(("startup delay occurs once and is independent of playback speed",async()=>{var sink=new FakeSink();var s=new SessionMachine();var sw=Stopwatch.StartNew();await new PlaybackEngine(sink,s).PlayAsync([new(){Kind=ActionKind.Text,Text="x",Enabled=false},new(){Kind=ActionKind.Text,Text="y"}],new(){Repetitions=2,Speed=100},startDelayMs:60);Assert(sw.ElapsedMilliseconds>=55&&sw.ElapsedMilliseconds<500);Assert(sink.Log.Count(x=>x=="Uy+")==2);}));
tests.Add(("playback ordering, disabled actions, Unicode, speed and repeats",async()=>{var sink=new FakeSink();var s=new SessionMachine();var e=new PlaybackEngine(sink,s);await e.PlayAsync([new(){Kind=ActionKind.KeyChord,Keys=[17,65]},new(){Kind=ActionKind.Text,Text="λ"},new(){Kind=ActionKind.Text,Text="NO",Enabled=false}],new(){Repetitions=2,Speed=2});Assert(string.Join(" ",sink.Log.Where(x=>x!="release"))=="K17+ K65+ K65- K17- Uλ+ Uλ- K17+ K65+ K65- K17- Uλ+ Uλ-");Assert(s.State==SessionState.Idle&&sink.Releases==3);}));
tests.Add(("cancellation interrupts long waits and releases modifiers/buttons",async()=>{var sink=new FakeSink();var s=new SessionMachine();var e=new PlaybackEngine(sink,s);using var cancel=new CancellationTokenSource(60);var sw=Stopwatch.StartNew();try{await e.PlayAsync([new(){Kind=ActionKind.Key,VirtualKey=17,IsDown=true},new(){Kind=ActionKind.MouseButton,IsDown=true},new(){Kind=ActionKind.Delay,DurationMs=60000}],new(),cancel.Token);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}Assert(sw.ElapsedMilliseconds<1000&&sink.Releases==1&&s.State==SessionState.Idle);Assert(sink.Log.Contains("K17+")&&sink.Log.Contains("BLeft+"));}));
tests.Add(("stop terminates infinite zero-duration repeats",async()=>{var sink=new FakeSink();var s=new SessionMachine();var e=new PlaybackEngine(sink,s);var task=e.PlayAsync([new(){Kind=ActionKind.Text,Text="x"}],new(){UntilStopped=true});await Task.Delay(30);e.Stop();try{await task;}catch(OperationCanceledException){}Assert(sink.Releases>1&&s.State==SessionState.Idle);}));
tests.Add(("pause freezes monotonic timeline; cancellation interrupts pause",async()=>{var c=new PlaybackClock();await Task.Delay(10);c.Pause();double at=c.Elapsed;await Task.Delay(30);Assert(Math.Abs(c.Elapsed-at)<2);using var token=new CancellationTokenSource(30);try{await c.WaitUntilAsync(1000,token.Token);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}c.Resume();Assert(c.Elapsed<40);}));
tests.Add(("injection failure still invokes cleanup and resets session",async()=>{var sink=new FakeSink(){Fail=true};var s=new SessionMachine();try{await new PlaybackEngine(sink,s).PlayAsync([new(){Kind=ActionKind.Key,VirtualKey=65,IsDown=true}],new());throw new Exception("Expected failure");}catch(InvalidOperationException){}Assert(sink.Releases==1&&s.State==SessionState.Idle);}));
int failed=0;foreach(var (name,run) in tests){try{await run();Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}Console.WriteLine($"{tests.Count-failed}/{tests.Count} passed");return failed==0?0:1;

sealed class FakeSink : IInputSink {
    public List<string> Log=[];public int Releases;public bool Fail;
    public void Move(int x,int y,MacroAction a)=>Log.Add($"M{x},{y}");
    public void Button(MouseButton b,bool d)=>Log.Add($"B{b}{(d?"+":"-")}");
    public void Scroll(int d,bool h)=>Log.Add($"S{d}");
    public void Key(int v,int s,bool x,bool d){Log.Add($"K{v}{(d?"+":"-")}");if(Fail)throw new InvalidOperationException("injection rejected");}
    public void Unicode(char c,bool d)=>Log.Add($"U{c}{(d?"+":"-")}");
    public void Focus(WindowTarget t,bool r)=>Log.Add("focus");
    public void ReleaseAll(){Releases++;Log.Add("release");}
}
