using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using MacroStudio.Core;
using MacroStudio.Windows;

// Explicitly opt-in: exercises only a freshly launched Interaction Lab on an unlocked interactive desktop.
if(args.Length!=3){Console.Error.WriteLine("Usage: NativeTests <dotnet.exe> <TestLab.dll> <evidence-directory>");return 2;}
Native.SetProcessDpiAwarenessContext(-4);
var directory=Path.GetFullPath(args[2]);Directory.CreateDirectory(directory);var probe=Path.Combine(directory,"lab-state.json");if(File.Exists(probe))File.Delete(probe);
bool executable=args[1].EndsWith(".exe",StringComparison.OrdinalIgnoreCase);
var start=new ProcessStartInfo(Path.GetFullPath(executable?args[1]:args[0])){UseShellExecute=false,CreateNoWindow=true};if(!executable)start.ArgumentList.Add(Path.GetFullPath(args[1]));start.ArgumentList.Add("--probe");start.ArgumentList.Add(probe);
using var lab=Process.Start(start)??throw new Exception("Lab launch failed");
var results=new List<string>();WindowsInput sink=new();
void Assert(bool condition,string detail){if(!condition)throw new Exception(detail);}
void Pass(string detail){results.Add(detail);Console.WriteLine("PASS "+detail);}
async Task<JsonElement> Snapshot(){for(int i=0;i<1000;i++){try{return JsonDocument.Parse(await File.ReadAllTextAsync(probe)).RootElement.Clone();}catch(IOException){}catch(JsonException){}if(lab.HasExited)throw new Exception($"Lab exited with code {lab.ExitCode} before telemetry was ready.");await Task.Delay(30);}throw new Exception("Lab telemetry unavailable after 30 seconds");}
async Task<JsonElement> Until(Func<JsonElement,bool> condition){for(int i=0;i<120;i++){var s=await Snapshot();if(condition(s))return s;await Task.Delay(25);}throw new Exception("Lab did not reach expected state: "+(await Snapshot()).GetRawText());}
int[] Point(JsonElement s,string key)=>s.GetProperty(key).EnumerateArray().Select(v=>v.GetInt32()).ToArray();
nint hwnd=0;
void Guard(){Assert(Native.GetForegroundWindow()==hwnd,$"Integration input aborted: the lab lost foreground focus (lab {hwnd}, foreground {Native.GetForegroundWindow()}, desktop {Native.GetSystemMetrics(78)}×{Native.GetSystemMetrics(79)}).");}
void Emit(Native.Input input){Guard();Assert(Native.SendInput(1,[input],Marshal.SizeOf<Native.Input>())==1,"Test-source SendInput failed");}
void Key(int vk,bool down)=>Emit(new(){Type=1,Data=new(){Key=new(){Vk=(ushort)vk,Flags=down?0u:2u,Extra=0x54455354}}});
void Move(int[] p)=>Emit(new(){Data=new(){Mouse=new(){X=WindowsInput.Normalize(p[0],Native.GetSystemMetrics(76),Native.GetSystemMetrics(78)),Y=WindowsInput.Normalize(p[1],Native.GetSystemMetrics(77),Native.GetSystemMetrics(79)),Flags=0xC001,Extra=0x54455354}}});
void Mouse(uint flags,uint data=0)=>Emit(new(){Data=new(){Mouse=new(){Flags=flags,Data=data,Extra=0x54455354}}});
async Task Click(int[] p,uint down=2,uint up=4){Move(p);await Task.Delay(25);Mouse(down);await Task.Delay(35);Mouse(up);await Task.Delay(60);}
async Task Shortcut(int key,bool modifiers=false){if(modifiers){Key(162,true);Key(164,true);}Key(key,true);await Task.Delay(20);Key(key,false);if(modifiers){Key(164,false);Key(162,false);}await Task.Delay(50);}
try {
    var state=await Snapshot();
    for(int i=0;i<100&&hwnd==0;i++){lab.Refresh();hwnd=lab.MainWindowHandle;await Task.Delay(20);}
    Assert(hwnd!=0,"No lab window");var target=WindowCatalog.Describe(hwnd)??throw new Exception("No target metadata");
    Console.WriteLine($"DPI diagnostics: lab bounds {target.Left},{target.Top} {target.Width}×{target.Height}; virtual desktop {Native.GetSystemMetrics(78)}×{Native.GetSystemMetrics(79)}; entry {string.Join(',',Point(state,"Entry"))}");
    Console.WriteLine("WAIT: activate the freshly launched Interaction Lab. Input begins only while it owns focus.");
    var gatePath=Environment.GetEnvironmentVariable("MACROSTUDIO_TEST_GATE");
    if(!string.IsNullOrWhiteSpace(gatePath)) {
        Console.WriteLine("WAIT: test-start gate "+gatePath);
        for(int i=0;i<1200&&!File.Exists(gatePath);i++)await Task.Delay(100);
        Assert(File.Exists(gatePath),"Test-start gate was not released; no input sent.");
    }
    for(int i=0;i<600&&Native.GetForegroundWindow()!=hwnd;i++)await Task.Delay(100);
    Assert(Native.GetForegroundWindow()==hwnd,"Lab was not activated within 60 seconds; no test input was sent.");await Task.Delay(100);
    using var hooks=new HookService();var session=new SessionMachine();using var recorder=new RecordingService(hooks,session);var engine=new PlaybackEngine(sink,session);
    await hooks.ConfigureAsync([new("Record",3,117),new("Play",3,118),new("Pause",3,122),new("Stop",0,116)]);
    try{await hooks.ConfigureAsync([new("Record",0,117),new("Play",0,117),new("Pause",0,122),new("Stop",0,116)]);throw new Exception("Duplicate shortcuts were accepted");}catch(InvalidOperationException){}
    using(var conflicting=new HookService()){try{await conflicting.ConfigureAsync([new("Record",3,117),new("Play",3,118),new("Pause",3,122),new("Stop",0,116)]);throw new Exception("OS shortcut conflict was accepted");}catch(InvalidOperationException){}}
    Pass("global shortcuts reject duplicate assignments and actual OS registration conflicts");
    Task<List<MacroAction>>? stoppedRecording=null;var pauseSeen=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    hooks.Control+=command=>{if(command=="Stop"){if(session.State==SessionState.Recording)stoppedRecording=recorder.StopAsync();else engine.Stop();}if(command=="Pause"){engine.TogglePause();pauseSeen.TrySetResult();}};
    await recorder.StartAsync(CoordinateMode.Window);
    await Click(Point(state,"Entry"));Key(160,true);Key(65,true);await Task.Delay(25);Key(65,false);Key(160,false);
    await Click(Point(state,"Counter"));await Click(Point(state,"Counter"));
    // Use a button for auxiliary clicks: middle-clicking a text editor can enter Windows autoscroll mode.
    await Click(Point(state,"Counter"),8,16);await Click(Point(state,"Counter"),32,64);
    Move(Point(state,"ScrollPoint"));await Task.Delay(80);Mouse(0x800,unchecked((uint)-120));await Task.Delay(100);
    Console.WriteLine("Wheel delivery diagnostic: "+(await Snapshot()).GetRawText());
    var from=Point(state,"Drag");var to=Point(state,"Drop");Move(from);Mouse(2);for(int i=1;i<=12;i++){Move([from[0]+(to[0]-from[0])*i/12,from[1]+(to[1]-from[1])*i/12]);await Task.Delay(15);}Mouse(4);await Task.Delay(60);
    await Shortcut(122,true); // configured control chord: must be excluded with its Ctrl/Alt edges
    await Shortcut(116);
    for(int i=0;i<100&&stoppedRecording is null;i++)await Task.Delay(10);
    Assert(stoppedRecording!=null,"Global stop did not stop recording");var actions=await stoppedRecording!;
    Assert(actions.Any(a=>a.Kind==ActionKind.MousePath&&a.Path.Count>=10),"Drag path not consolidated");
    Assert(actions.Where(a=>a.Kind==ActionKind.MouseButton).Select(a=>a.Button).Distinct().Count()==3,"Missing left/right/middle button");
    Assert(actions.Any(a=>a.Kind==ActionKind.Scroll),"Missing wheel event");
    Assert(actions.Where(a=>a.Kind==ActionKind.Key).Select(a=>a.VirtualKey).SequenceEqual(new[]{160,65,65,160}),"Control hotkeys or modifier edges leaked, or key order changed");
    Assert(actions.All(a=>a.Coordinates==CoordinateMode.Window||a.Kind==ActionKind.FocusWindow),"Relative recording mode was lost");
    Pass("real low-level hooks: mouse paths, three buttons, drag, scroll, Shift+A and global shortcut exclusion");
    var first=await Until(s=>s.GetProperty("Text").GetString()=="A"&&s.GetProperty("Count").GetInt32()==2&&s.GetProperty("DragX").GetDouble()>440&&s.GetProperty("Scroll").GetDouble()>0);
    Pass("external lab received the recorded text, two clicks and drag");
    actions[0].DelayMs=0;actions[0].Comment="Edited after native recording, then atomically saved and reopened.";
    var macroPath=Path.Combine(directory,"recorded-edited-reopened.json");await MacroStore.SaveAsync(macroPath,new(){Name="Native lab recording",Actions=actions});var reopened=await MacroStore.LoadAsync(macroPath);Assert(reopened.Actions[0].Comment==actions[0].Comment,"Edited step lost");
    await Click(Point(first,"Reset"));await Until(s=>s.GetProperty("Text").GetString()==""&&s.GetProperty("Count").GetInt32()==0);
    await engine.PlayAsync(reopened.Actions,new(){RestoreWindows=true});
    var replay=await Until(s=>s.GetProperty("Text").GetString()=="A"&&s.GetProperty("Count").GetInt32()==2&&s.GetProperty("DragX").GetDouble()>440&&s.GetProperty("Scroll").GetDouble()>0);
    Pass("record → stop → edit → atomic save → reopen → SendInput replay reproduced text/counter/scroll/drag");
    await recorder.StartAsync(CoordinateMode.Desktop);sink.Move(Point(replay,"Entry")[0],Point(replay,"Entry")[1],new());sink.Key(66,0,false,true);sink.Key(66,0,false,false);sink.Button(MouseButton.Right,true);sink.Button(MouseButton.Right,false);await Task.Delay(70);var injected=await recorder.StopAsync();Assert(!injected.Any(a=>a.Kind is ActionKind.Key or ActionKind.MouseButton or ActionKind.MousePath),"Engine input self-recorded");Pass("tagged playback input is excluded by actual hooks");
    await Click(Point(replay,"Entry"));
    var longWait=engine.PlayAsync([new(){Kind=ActionKind.Key,VirtualKey=162,IsDown=true},new(){Kind=ActionKind.MouseButton,X=Point(replay,"Entry")[0],Y=Point(replay,"Entry")[1],IsDown=true},new(){Kind=ActionKind.Delay,DurationMs=60000}],new());await Task.Delay(80);
    Assert((Native.GetAsyncKeyState(162)&0x8000)!=0&&(Native.GetAsyncKeyState(1)&0x8000)!=0,"Engine did not hold Ctrl and mouse before cancellation");var stopWatch=Stopwatch.StartNew();await Shortcut(116);try{await longWait;throw new Exception("Long delay did not cancel");}catch(OperationCanceledException){}
    Assert(stopWatch.ElapsedMilliseconds<600,"Emergency stop too slow");Assert((Native.GetAsyncKeyState(162)&0x8000)==0&&(Native.GetAsyncKeyState(1)&0x8000)==0,"Held modifier/button was not released");Pass("global emergency stop interrupts a 60-second delay and releases real Ctrl and left button");
    int repetition=0;engine.Progress+=(_,r)=>repetition=r;var repeat=engine.PlayAsync([new(){Kind=ActionKind.Delay,DurationMs=15}],new(){UntilStopped=true});for(int i=0;i<100&&repetition<3;i++)await Task.Delay(10);Assert(repetition>=3,"Repeat did not run");await Shortcut(116);try{await repeat;}catch(OperationCanceledException){}Assert(session.State==SessionState.Idle,"Repeat failed to stop");Pass("global emergency stop terminates an active infinite repeat");
    var paused=engine.PlayAsync([new(){Kind=ActionKind.Delay,DurationMs=60000}],new());await Task.Delay(30);await Shortcut(122,true);await pauseSeen.Task.WaitAsync(TimeSpan.FromSeconds(2));Assert(session.State==SessionState.Paused,"Global pause did not freeze playback");await Shortcut(122,true);Assert(session.State==SessionState.Playing,"Global resume did not resume playback");await Shortcut(122,true);Assert(session.State==SessionState.Paused,"Second pause failed");await Shortcut(116);try{await paused;}catch(OperationCanceledException){}Pass("global pause, resume and stop work while the external lab owns focus");
    await Click(Point(await Snapshot(),"Reset"));await Until(s=>s.GetProperty("Text").GetString()=="");state=await Snapshot();var entry=Point(state,"Entry");
    Assert(Native.SetWindowPos(hwnd,0,target.Left+45,target.Top+30,target.Width,target.Height,0x14),"Could not move lab");
    await engine.PlayAsync([new(){Kind=ActionKind.FocusWindow,Target=target},new(){Kind=ActionKind.MouseButton,Coordinates=CoordinateMode.Window,Target=target,X=entry[0]-target.Left,Y=entry[1]-target.Top,IsDown=true},new(){Kind=ActionKind.MouseButton,Coordinates=CoordinateMode.Window,Target=target,X=entry[0]-target.Left,Y=entry[1]-target.Top,IsDown=false},new(){Kind=ActionKind.Text,Text="λ 日本語 🪟",DelayMs=30}],new(){RestoreWindows=true});
    var unicode=await Until(s=>s.GetProperty("Text").GetString()=="λ 日本語 🪟");Native.GetWindowRect(hwnd,out var restored);Assert(restored.Left==target.Left&&restored.Top==target.Top,"Window bounds were not restored");Pass($"Unicode SendInput and window-relative coordinates/restoration at {unicode.GetProperty("Dpi").GetDouble():0.##}× DPI");
    await File.WriteAllTextAsync(Path.Combine(directory,"results.json"),JsonSerializer.Serialize(new{Passed=results,Macro=macroPath,RecordedSteps=actions.Count,Snapshot=unicode},new JsonSerializerOptions(){WriteIndented=true}));
    Console.WriteLine($"{results.Count} native checks passed; evidence in {directory}");return 0;
} catch(Exception ex){Console.Error.WriteLine(ex);await File.WriteAllTextAsync(Path.Combine(directory,"failure.txt"),ex.ToString());return 1;}
finally{try{sink.ReleaseAll();}catch{}lab.CloseMainWindow();if(!lab.WaitForExit(3000))Console.Error.WriteLine("Lab is still open; close it manually.");}
