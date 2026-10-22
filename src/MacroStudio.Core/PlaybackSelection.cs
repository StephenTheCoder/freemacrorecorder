using System.Text.Json;
namespace MacroStudio.Core;

public static class PlaybackSelection
{
    public static (List<MacroAction> Actions, PlaybackOptions Options) Create(MacroDocument document,IEnumerable<Guid> selected,string mode)
    {
        var ids=selected.ToHashSet();IEnumerable<MacroAction> actions=document.Actions;
        if(mode is not ("all" or "one" or "range" or "from"))throw new ArgumentException("Unknown playback selection mode.");
        if(mode!="all"&&!document.Actions.Any(a=>ids.Contains(a.Id)))throw new InvalidOperationException("Select at least one step first.");
        if(mode=="one")actions=document.Actions.Where(a=>ids.Contains(a.Id)).Take(1);
        if(mode=="range"){int first=document.Actions.FindIndex(a=>ids.Contains(a.Id)),last=document.Actions.FindLastIndex(a=>ids.Contains(a.Id));actions=document.Actions.Skip(first).Take(last-first+1);}
        if(mode=="from")actions=document.Actions.SkipWhile(a=>!ids.Contains(a.Id));
        var options=JsonSerializer.Deserialize<PlaybackOptions>(JsonSerializer.Serialize(document.Playback,MacroStore.Json),MacroStore.Json)!;
        if(mode=="one"){options.Repetitions=1;options.UntilStopped=false;}
        return (actions.Select(MacroStore.Clone).ToList(),options);
    }
}
