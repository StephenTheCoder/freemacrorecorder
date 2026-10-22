namespace MacroStudio.Core;
public enum TimeoutPolicy { Stop, Retry, Jump }
public sealed class FlowSettings
{
    public string Label {get;set;}="";
    public string Destination {get;set;}="";
    public string Otherwise {get;set;}="";
    public int Count {get;set;}=2;
    public string MacroFile {get;set;}="";
    public TimeoutPolicy Timeout {get;set;}
    public int Retries {get;set;}=1;
}
public sealed class MacroProgram
{
    public List<MacroAction> Actions {get;init;}=[];
    public Dictionary<string,int> Labels {get;}=new(StringComparer.Ordinal);
    public Dictionary<int,int> Repeats {get;}=[];
    public Dictionary<int,MacroProgram> Calls {get;}=[];
    public string Directory {get;init;}="";
    public static async Task<MacroProgram> BuildAsync(IEnumerable<MacroAction> source,string directory,string? rootFile=null,CancellationToken token=default) {
        var chain=new HashSet<string>(StringComparer.OrdinalIgnoreCase);if(!string.IsNullOrWhiteSpace(rootFile))chain.Add(Path.GetFullPath(rootFile));int total=0;
        async Task<MacroProgram> Load(IEnumerable<MacroAction> actions,string folder,int depth){
            token.ThrowIfCancellationRequested();if(depth>12)throw new InvalidDataException("Macro calls exceed the 12-level nesting limit.");
            var program=new MacroProgram{Actions=actions.Where(a=>a.Enabled).Select(MacroStore.Clone).ToList(),Directory=folder};
            total+=program.Actions.Count;if(total>200000)throw new InvalidDataException("Expanded macro program exceeds 200000 actions.");
            MacroStore.Validate(new(){Actions=program.Actions});
            var blocks=new Stack<int>();var scopes=new Dictionary<int,string>();
            for(int i=0;i<program.Actions.Count;i++){
                var a=program.Actions[i];scopes[i]=string.Join('/',blocks.Reverse());
                if(a.Bindings.Values.Any(v=>!v.Contains("${"))){
                    var literal=MacroStore.Clone(a);literal.Bindings=a.Bindings.Where(v=>!v.Value.Contains("${")).ToDictionary();
                    literal.Text="";literal.Comment="";literal.Detection.Image="";literal.Detection.Color="#FFFFFF";literal.Data.Value="";literal.Data.Operand="0";literal.Data.File="";literal.Data.Language="en-US";
                    if(literal.Target is {} target)literal.Target=target with {ProcessName="validation",Title=""};
                    try{new VariableStore().Resolve(literal);}catch(System.Text.Json.JsonException ex){throw new InvalidDataException("Invalid literal property binding.",ex);}
                }
                if(a.Kind==ActionKind.Label){if(string.IsNullOrWhiteSpace(a.Flow.Label)||!program.Labels.TryAdd(a.Flow.Label,i))throw new InvalidDataException("Labels must be nonempty and unique: "+a.Flow.Label);}
                if(a.Kind==ActionKind.RepeatStart)blocks.Push(i);
                if(a.Kind==ActionKind.RepeatEnd){if(!blocks.TryPop(out var begin))throw new InvalidDataException("Repeat end has no start.");program.Repeats[begin]=i;program.Repeats[i]=begin;}
                if(a.Kind is ActionKind.ImageClick or ActionKind.WaitImage or ActionKind.WaitImageGone){if(string.IsNullOrWhiteSpace(a.Detection.Image))throw new InvalidDataException($"{a.Kind} needs a template image.");if(!a.Detection.Image.Contains("${")&&!File.Exists(Path.GetFullPath(a.Detection.Image,folder)))throw new InvalidDataException("Missing template image: "+a.Detection.Image);}
                if(a.Kind==ActionKind.WaitWindow&&a.Target is null)throw new InvalidDataException("WaitWindow requires a window target.");
                if(a.Kind==ActionKind.CallMacro){
                    if(string.IsNullOrWhiteSpace(a.Flow.MacroFile)||a.Flow.MacroFile.Contains("${"))throw new InvalidDataException("Macro calls require a static file path so recursion can be validated before playback.");
                    var path=Path.GetFullPath(a.Flow.MacroFile,folder);if(!chain.Add(path))throw new InvalidDataException("Recursive macro call: "+path);
                    var doc=await MacroStore.LoadAsync(path);program.Calls[i]=await Load(doc.Actions,Path.GetDirectoryName(path)!,depth+1);chain.Remove(path);
                }
                if(a.Kind is ActionKind.SetVariable or ActionKind.Calculate or ActionKind.Compare or ActionKind.OcrText or ActionKind.OcrFind or ActionKind.ClipboardRead or ActionKind.ReadText or ActionKind.ReadCsv or ActionKind.AiText or ActionKind.AiImage)VariableStore.ValidateName(a.Data.Variable);
                if(a.Kind is ActionKind.Calculate && a.Data.Operation is not ("+" or "-" or "*" or "/" or "%"))throw new InvalidDataException("Invalid arithmetic operation.");
                if(a.Kind is ActionKind.Compare or ActionKind.Branch && a.Data.Operation is not ("==" or "!=" or "contains" or ">" or "<" or ">=" or "<="))throw new InvalidDataException("Invalid comparison operation.");
                if(a.Kind is ActionKind.ReadText or ActionKind.WriteText or ActionKind.ReadCsv or ActionKind.WriteCsv && string.IsNullOrWhiteSpace(a.Data.File))throw new InvalidDataException("File actions require a file path.");
                if(a.Kind==ActionKind.Screenshot&&string.IsNullOrWhiteSpace(a.Data.Variable)&&string.IsNullOrWhiteSpace(a.Data.File))throw new InvalidDataException("Screenshot needs an output file or image variable.");
                if(a.Kind==ActionKind.SetVariable&&!a.Data.Value.Contains("${"))VariableValue.Create(a.Data.Type,a.Data.Value);
                if(a.Kind==ActionKind.SetVariable&&a.Data.Type==VariableType.Image&&!a.Data.Value.Contains("${")&&!File.Exists(Path.GetFullPath(a.Data.Value,folder)))throw new InvalidDataException("Missing image reference: "+a.Data.Value);
                if(a.Kind==ActionKind.Calculate&&!a.Data.Value.Contains("${")&&!a.Data.Operand.Contains("${"))DataOperations.Execute(a,new());
                if(a.Kind is ActionKind.Compare or ActionKind.Branch&&!a.Data.Value.Contains("${")&&!a.Data.Operand.Contains("${"))DataOperations.Compare(a.Data.Value,a.Data.Operation,a.Data.Operand);
            }
            if(blocks.Count!=0)throw new InvalidDataException("Repeat start has no matching end.");
            for(int i=0;i<program.Actions.Count;i++){
                var a=program.Actions[i];var targets=new List<string>();if(a.Kind is ActionKind.Jump or ActionKind.Branch || a.Flow.Timeout==TimeoutPolicy.Jump)targets.Add(a.Flow.Destination);if(a.Kind==ActionKind.Branch&&!string.IsNullOrEmpty(a.Flow.Otherwise))targets.Add(a.Flow.Otherwise);
                foreach(var label in targets){if(!program.Labels.TryGetValue(label,out var j))throw new InvalidDataException("Missing label: "+label);if(scopes[i]!=scopes[j])throw new InvalidDataException("Jumps cannot enter or leave repeat blocks. Put the label in the same block.");}
            }
            return program;
        }
        return await Load(source,Path.GetFullPath(directory),0);
    }
}
public static class MousePaths
{
    public static IReadOnlyList<PathPoint> Shape(MacroAction a,MousePathShape shape){
        if(shape==MousePathShape.Recorded||a.Path.Count<2)return a.Path;
        var first=a.Path[0];var last=a.Path[^1];var points=new List<PathPoint>();int steps=(int)Math.Clamp(Math.Ceiling(a.DurationMs/10),2,10000);
        double dx=last.X-first.X,dy=last.Y-first.Y,bend=Math.Min(80,Math.Sqrt(dx*dx+dy*dy)*.15);
        for(int i=0;i<=steps;i++){double t=i/(double)steps,s=shape==MousePathShape.Curved?t*t*(3-2*t):t,arc=shape==MousePathShape.Curved?4*t*(1-t)*bend:0,len=Math.Max(1,Math.Sqrt(dx*dx+dy*dy));points.Add(new((int)Math.Round(first.X+dx*s-dy/len*arc),(int)Math.Round(first.Y+dy*s+dx/len*arc),a.DurationMs*t));}return points;
    }
}
