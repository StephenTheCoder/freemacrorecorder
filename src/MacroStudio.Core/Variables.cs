using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MacroStudio.Core;
public enum VariableType { Text, Number, Boolean, Coordinates, Image }
public sealed record VariableValue(VariableType Type,string Value)
{
    public static VariableValue Create(VariableType type,string value) {
        switch(type){
            case VariableType.Number:if(!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n))throw new InvalidDataException("Number variables must be finite invariant-culture numbers.");value=n.ToString("R",CultureInfo.InvariantCulture);break;
            case VariableType.Boolean:value=bool.Parse(value).ToString().ToLowerInvariant();break;
            case VariableType.Coordinates:var xy=value.Split(',');if(xy.Length!=2||!int.TryParse(xy[0],out _)||!int.TryParse(xy[1],out _))throw new InvalidDataException("Coordinates use X,Y physical integer pixels.");break;
            case VariableType.Image:if(string.IsNullOrWhiteSpace(value))throw new InvalidDataException("Image reference cannot be empty.");break;
        }
        return new(type,value);
    }
}
public sealed class DataSettings
{
    public string Variable {get;set;}="result";
    public VariableType Type {get;set;}
    public string Value {get;set;}="";
    public string Operand {get;set;}="0";
    public string Operation {get;set;}="+";
    public string File {get;set;}="";
    public bool Append {get;set;}
    public int Row {get;set;}
    public int Column {get;set;}
    public string Language {get;set;}="en-US";
}
public sealed class VariableStore
{
    readonly ConcurrentDictionary<string,VariableValue> values=new(StringComparer.Ordinal);
    public event Action? Changed;
    public IReadOnlyDictionary<string,VariableValue> Snapshot()=>new Dictionary<string,VariableValue>(values);
    public static void ValidateName(string name){if(!Regex.IsMatch(name,@"^[A-Za-z_][A-Za-z0-9_]{0,63}$"))throw new InvalidDataException("Variable names use letters, digits and underscore, starting with a letter or underscore (max 64).");}
    public void Set(string name,VariableValue value){ValidateName(name);values[name]=VariableValue.Create(value.Type,value.Value);Changed?.Invoke();}
    public VariableValue Get(string name)=>values.TryGetValue(name,out var value)?value:throw new InvalidOperationException($"Variable '{name}' is not defined.");
    public string Substitute(string input)=>Regex.Replace(input,@"\$\{([A-Za-z_][A-Za-z0-9_]*)(?:\.(x|y))?\}",m=>{var v=Get(m.Groups[1].Value);if(!m.Groups[2].Success)return v.Value;if(v.Type!=VariableType.Coordinates)throw new InvalidOperationException("Only coordinate variables have .x and .y properties.");return v.Value.Split(',')[m.Groups[2].Value=="x"?0:1].Trim();});
    public MacroAction Resolve(MacroAction source) {
        var node=JsonSerializer.SerializeToNode(source,MacroStore.Json)!;
        // Substitute only data-bearing strings; output names and structural flow labels stay literal.
        foreach(var path in new[]{"text","comment","target.processName","target.title","detection.image","detection.color","data.value","data.operand","data.file","data.language"}) {
            var parts=path.Split('.');var parent=parts.Length==1?node:node[parts[0]];var key=parts[^1];if(parent?[key] is JsonValue v)parent[key]=Substitute(v.GetValue<string>());
        }
        foreach(var(binding,expression)in source.Bindings){
            if(!AllowedBindings.Contains(binding))throw new InvalidDataException($"Property binding '{binding}' is not supported.");
            var parts=binding.Split('.');var parent=node;foreach(var part in parts.SkipLast(1))parent=parent[part]??throw new InvalidDataException($"Set {part} before binding its properties.");var key=parts[^1];var raw=Substitute(expression);
            parent[key]=JsonNode.Parse(raw)??throw new InvalidDataException("Property binding evaluated to null.");
        }
        var resolved=node.Deserialize<MacroAction>(MacroStore.Json)!;MacroStore.Validate(new(){Actions=[resolved]});return resolved;
    }
    public static readonly HashSet<string> AllowedBindings=["x","y","delayMs","durationMs","wheelDelta","virtualKey","scanCode","isDown","extended","detection.threshold","detection.timeoutMs","detection.templateDpi","detection.offsetX","detection.offsetY","detection.region.x","detection.region.y","detection.region.width","detection.region.height","data.row","data.column","flow.count","flow.retries"];
}
public static class DataOperations
{
    public static bool Compare(string left,string operation,string right)=>operation switch {
        "=="=>left==right,"!="=>left!=right,"contains"=>left.Contains(right,StringComparison.Ordinal),
        ">"=>Number(left)>Number(right),"<"=>Number(left)<Number(right),">="=>Number(left)>=Number(right),"<="=>Number(left)<=Number(right),
        _=>throw new InvalidDataException("Comparison must be ==, !=, contains, >, <, >= or <=.")
    };
    public static double Number(string value)=>double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)?n:throw new InvalidDataException($"Expected a finite number: '{value}'.");
    public static bool Execute(MacroAction a,AutomationContext context) {
        var d=a.Data;var v=context.Variables;
        switch(a.Kind){
            case ActionKind.SetVariable:v.Set(d.Variable,VariableValue.Create(d.Type,d.Type==VariableType.Image?context.ResolvePath(d.Value):d.Value));return true;
            case ActionKind.Calculate:
                double l=Number(d.Value),r=Number(d.Operand);double result=d.Operation switch{"+"=>l+r,"-"=>l-r,"*"=>l*r,"/" when r!=0=>l/r,"%" when r!=0=>l%r,_=>throw new InvalidDataException("Use +, -, *, / or % with a nonzero divisor.")};v.Set(d.Variable,VariableValue.Create(VariableType.Number,result.ToString("R",CultureInfo.InvariantCulture)));return true;
            case ActionKind.Compare:v.Set(d.Variable,new(VariableType.Boolean,Compare(d.Value,d.Operation,d.Operand).ToString()));return true;
            default:return false;
        }
    }
}
public static class CsvData
{
    public static List<List<string>> Parse(string text) {
        var rows=new List<List<string>>();var row=new List<string>();var field=new StringBuilder();bool quoted=false,closed=false;
        for(int i=0;i<text.Length;i++){char c=text[i];if(quoted){if(c=='"'){if(i+1<text.Length&&text[i+1]=='"'){field.Append('"');i++;}else{quoted=false;closed=true;}}else field.Append(c);}else if(c=='"'&&field.Length==0&&!closed)quoted=true;else if(c==','||c=='\r'||c=='\n'){row.Add(field.ToString());field.Clear();closed=false;if(c!=','){rows.Add(row);row=[];if(c=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;}}else{if(closed)throw new InvalidDataException("Invalid CSV after closing quote.");field.Append(c);}}
        if(quoted)throw new InvalidDataException("Unclosed CSV quote.");if(field.Length>0||row.Count>0||closed){row.Add(field.ToString());rows.Add(row);}return rows;
    }
    public static string Serialize(IEnumerable<IEnumerable<string>> rows)=>string.Join("\r\n",rows.Select(r=>string.Join(",",r.Select(v=>"\""+v.Replace("\"","\"\"")+"\""))))+"\r\n";
}
