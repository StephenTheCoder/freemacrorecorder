using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using MacroStudio.Core;
using MacroStudio.Windows;

namespace MacroStudio;
public class Bindable : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    protected bool Set<T>(ref T field,T value,[CallerMemberName]string? name=null) {if(EqualityComparer<T>.Default.Equals(field,value))return false;field=value;Notify(name);return true;}
}
public sealed class RelayCommand(Action execute,Func<bool>? can=null) : ICommand {
    public bool CanExecute(object? p)=>can?.Invoke()??true; public void Execute(object? p)=>execute();
    public event EventHandler? CanExecuteChanged {add=>CommandManager.RequerySuggested+=value;remove=>CommandManager.RequerySuggested-=value;}
}
public sealed class ActionRow(MacroAction action,int number,string section="Actions") : Bindable {
    public MacroAction Action {get;}=action; public int Number {get;}=number;
    public string Section=>section;public string Marker=>Action.Breakpoint?"●":"";
    public bool Enabled=>Action.Enabled;public string Kind=>Action.Kind.ToString();public string Description=>Action.Description;
    public string Timing=>$"+{Action.DelayMs:0} / {Action.DurationMs:0} ms";public string Target=>Action.TargetLabel;public string Comment=>Action.Comment;
    bool running;public bool Running {get=>running;set=>Set(ref running,value);}
}
public sealed class StudioViewModel : Bindable, IDisposable {
    public SessionMachine Session {get;}=new();
    public HookService Hooks {get;}
    public RecordingService Recorder {get;}
    public PlaybackEngine Engine {get;}
    public ObservableCollection<ActionRow> Rows {get;}=[];
    public List<ActionRow> Selected {get;set;}=[];
    public event Action<IReadOnlyList<Guid>>? RestoreSelection;
    public MacroDocument Document {get;private set;}=new();
    public Stack<string> UndoStack {get;}=new(); public Stack<string> RedoStack {get;}=new();
    string baseline="",status="Ready to record",search="",filePath="";
    int repetition; bool dark;
    CancellationTokenSource? pendingPlayback;
    public string Status {get=>status;set=>Set(ref status,value);}
    public string Search {get=>search;set {Set(ref search,value);Refresh();}}
    public string FilePath {get=>filePath;private set {filePath=value;Notify();Notify(nameof(Title));}}
    public bool Dirty=>MacroStore.Serialize(Document)!=baseline;
    public string Title=>$"{Document.Name}{(Dirty?" •":"")} — MacroStudio";
    public bool Idle=>Session.State==SessionState.Idle&&pendingPlayback is null;
    public string State=>Session.State==SessionState.Idle&&pendingPlayback!=null?"Validating":Session.State.ToString();
    public string Summary=>$"{Document.Actions.Count} steps · repetition {repetition} · {State}";
    public bool Dark {get=>dark;set=>Set(ref dark,value);}
    public CoordinateMode RecordCoordinates {get;set;}
    public double Speed {get=>Document.Playback.Speed;set {if(!double.IsFinite(value)||value<.05||value>100)throw new ArgumentException("Speed must be 0.05–100.");if(value==Speed)return;Before();Document.Playback.Speed=value;Changed();}}
    public int Repetitions {get=>Document.Playback.Repetitions;set {if(value<1||value>1000000)throw new ArgumentException("Repetitions must be 1–1000000.");if(value==Repetitions)return;Before();Document.Playback.Repetitions=value;Changed();}}
    public bool UntilStopped {get=>Document.Playback.UntilStopped;set {if(value==UntilStopped)return;Before();Document.Playback.UntilStopped=value;Changed();}}
    public bool SkipPaths {get=>Document.Playback.SkipMousePaths;set {if(value==SkipPaths)return;Before();Document.Playback.SkipMousePaths=value;Changed();}}
    public bool RestoreWindows {get=>Document.Playback.RestoreWindows;set {if(value==RestoreWindows)return;Before();Document.Playback.RestoreWindows=value;Changed();}}
    public MousePathShape PathShape {get=>Document.Playback.PathShape;set{if(value==PathShape)return;Before();Document.Playback.PathShape=value;Changed();}}
    public StudioViewModel() {
        Hooks=new();Recorder=new(Hooks,Session);var sink=new WindowsInput();Engine=new(sink,Session,new WindowsAutomationHost(sink));baseline=MacroStore.Serialize(Document);
        Session.Changed+=_=>Application.Current.Dispatcher.BeginInvoke(()=>{Notify(nameof(Idle));Notify(nameof(State));Notify(nameof(Summary));CommandManager.InvalidateRequerySuggested();});
        Recorder.CountChanged+=count=>Application.Current.Dispatcher.BeginInvoke(()=>Status=$"Recording explicitly · {count} input samples captured");
        Engine.Progress+=(id,rep)=>Application.Current.Dispatcher.BeginInvoke(()=>{foreach(var row in Rows)row.Running=row.Action.Id==id;repetition=rep;Status=$"Playing {(Document.Actions.Any(a=>a.Id==id)?"step "+(Document.Actions.FindIndex(a=>a.Id==id)+1):"nested/library macro")} · repetition {rep}";Notify(nameof(Summary));});
    }
    public void Before() {UndoStack.Push(MacroStore.Serialize(Document));if(UndoStack.Count>100){var last=UndoStack.Reverse().Skip(1).ToArray();UndoStack.Clear();foreach(var s in last)UndoStack.Push(s);}RedoStack.Clear();}
    public void Changed(){Notify(nameof(Title));Notify(nameof(Dirty));Notify(nameof(Summary));CommandManager.InvalidateRequerySuggested();}
    public void Refresh() {var selection=Selected.Select(r=>r.Action.Id).ToHashSet();Rows.Clear();string section="Actions",previousGroup="\0";for(int i=0;i<Document.Actions.Count;i++){var a=Document.Actions[i];if(a.Group!=previousGroup){section=$"{(string.IsNullOrWhiteSpace(a.Group)?"Actions":a.Group)} / step {i+1}";previousGroup=a.Group;}if(string.IsNullOrWhiteSpace(search)||$"{a.Description} {a.TargetLabel} {a.Comment} {a.Kind}".Contains(search,StringComparison.OrdinalIgnoreCase))Rows.Add(new(a,i+1,section));}Selected=Rows.Where(r=>selection.Contains(r.Action.Id)).ToList();RestoreSelection?.Invoke(Selected.Select(r=>r.Action.Id).ToArray());Changed();}
    public void Replace(MacroDocument doc,string path="",bool saved=true){Document=doc;FilePath=path;UndoStack.Clear();RedoStack.Clear();if(saved)baseline=MacroStore.Serialize(doc);Refresh();OptionsChanged();}
    void OptionsChanged(){Notify(nameof(Speed));Notify(nameof(Repetitions));Notify(nameof(UntilStopped));Notify(nameof(SkipPaths));Notify(nameof(RestoreWindows));Notify(nameof(PathShape));}
    public void MarkSaved(string path){FilePath=path;baseline=MacroStore.Serialize(Document);Changed();}
    public void Undo(){if(!Idle||!UndoStack.TryPop(out var s))return;RedoStack.Push(MacroStore.Serialize(Document));Document=MacroStore.Parse(s);Refresh();OptionsChanged();}
    public void Redo(){if(!Idle||!RedoStack.TryPop(out var s))return;UndoStack.Push(MacroStore.Serialize(Document));Document=MacroStore.Parse(s);Refresh();OptionsChanged();}
    public void Insert(MacroAction action) {Before();int index=Selected.Count>0?Document.Actions.IndexOf(Selected[0].Action)+1:Document.Actions.Count;Document.Actions.Insert(index,action);Refresh();}
    public void Edit(Guid id,MacroAction replacement) {int i=Document.Actions.FindIndex(a=>a.Id==id);if(i<0)return;Before();replacement.Id=id;Document.Actions[i]=replacement;Refresh();}
    public void Delete(){if(!Idle||Selected.Count==0)return;Before();var ids=Selected.Select(r=>r.Action.Id).ToHashSet();Document.Actions.RemoveAll(a=>ids.Contains(a.Id));Refresh();}
    public void ToggleEnabled(){if(!Idle||Selected.Count==0)return;Before();bool enable=Selected.Any(r=>!r.Action.Enabled);foreach(var r in Selected)r.Action.Enabled=enable;Refresh();}
    public void Reorder(Guid target){if(!Idle||Selected.Count==0||Selected.Any(r=>r.Action.Id==target))return;var moving=Document.Actions.Where(a=>Selected.Any(s=>s.Action.Id==a.Id)).ToList();Before();Document.Actions.RemoveAll(a=>moving.Contains(a));int i=Document.Actions.FindIndex(a=>a.Id==target);Document.Actions.InsertRange(i<0?Document.Actions.Count:i,moving);Refresh();}
    public void MoveSelection(int delta){if(!Idle||Selected.Count==0)return;var selected=Selected.Select(r=>r.Action.Id).ToHashSet();Before();var indexes=Enumerable.Range(0,Document.Actions.Count).Where(i=>selected.Contains(Document.Actions[i].Id));foreach(var i in (delta>0?indexes.Reverse():indexes).ToArray()){int to=i+delta;if(to>=0&&to<Document.Actions.Count&&!selected.Contains(Document.Actions[to].Id))(Document.Actions[i],Document.Actions[to])=(Document.Actions[to],Document.Actions[i]);}Refresh();}
    public string Copy(){return MacroStore.Serialize(new(){Actions=Document.Actions.Where(a=>Selected.Any(r=>r.Action.Id==a.Id)).Select(MacroStore.Clone).ToList()});}
    public void Paste(string json){if(!Idle)return;var d=MacroStore.Parse(json);Before();int index=Selected.Count>0?Document.Actions.IndexOf(Selected.Last().Action)+1:Document.Actions.Count;foreach(var a in d.Actions)a.Id=Guid.NewGuid();Document.Actions.InsertRange(index,d.Actions);Refresh();}
    public async Task RecordAsync(){if(!Idle)return;await Recorder.StartAsync(RecordCoordinates);Status="Recording explicitly · use the global Stop shortcut when finished";}
    public async Task StopAsync(){if(Session.State==SessionState.Recording){var actions=await Recorder.StopAsync();Before();Document.Actions.AddRange(actions);Refresh();Status=$"Recorded {actions.Count} steps. {Recorder.VisualWarning}";}else {pendingPlayback?.Cancel();Engine.Stop();}}
    public async Task PlayAsync(string mode="all") {
        if(!Idle)return;
        var (list,options)=PlaybackSelection.Create(Document,Selected.Select(s=>s.Action.Id),mode);
        Engine.BaseDirectory=string.IsNullOrEmpty(FilePath)?Environment.CurrentDirectory:System.IO.Path.GetDirectoryName(FilePath)!;
        Engine.RootFile=string.IsNullOrEmpty(FilePath)?null:FilePath;
        using var pending=new CancellationTokenSource();pendingPlayback=pending;PlaybackBusyChanged();
        Status="Playback starts in 1 second · focus the target window";
        try {await Task.Run(()=>Engine.PlayAsync(list,options,pending.Token,startDelayMs:1000));Status="Playback complete";}catch(OperationCanceledException){Status="Playback stopped · held input released";}
        finally {pendingPlayback=null;PlaybackBusyChanged();foreach(var r in Rows)r.Running=false;}
    }
    void PlaybackBusyChanged(){Notify(nameof(Idle));Notify(nameof(State));Notify(nameof(Summary));CommandManager.InvalidateRequerySuggested();}
    public async Task PlayFileAsync(string path){if(!Idle)return;using var pending=new CancellationTokenSource();pendingPlayback=pending;PlaybackBusyChanged();try{var doc=await MacroStore.LoadAsync(path);Engine.BaseDirectory=System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;Engine.RootFile=path;Status="Running library macro: "+doc.Name;await Task.Run(()=>Engine.PlayAsync(doc.Actions,doc.Playback,pending.Token,startDelayMs:1000));Status="Library playback complete";}catch(OperationCanceledException){Status="Library playback stopped";}finally{pendingPlayback=null;PlaybackBusyChanged();}}
    public void Dispose(){pendingPlayback?.Cancel();Engine.Stop();Hooks.Dispose();Recorder.Dispose();}
}
