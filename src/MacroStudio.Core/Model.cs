using System.Text.Json.Serialization;

namespace MacroStudio.Core;

public enum ActionKind { MousePath, MouseButton, Scroll, Key, KeyChord, Text, Delay, FocusWindow,
    ImageClick, WaitImage, WaitImageGone, WaitPixel, WaitRegionChange, WaitWindow,
    Screenshot, OcrText, OcrFind, SetVariable, Calculate, Compare, ClipboardRead, ClipboardWrite, ReadText, WriteText, ReadCsv, WriteCsv,
    Label, Jump, Branch, RepeatStart, RepeatEnd, CallMacro, AiText, AiImage }
public enum CoordinateMode { Desktop, Window }
public enum MouseButton { Left, Right, Middle }
public record PathPoint(int X, int Y, double OffsetMs);
public sealed record WindowTarget(string ProcessName, string Title, int Left, int Top, int Width, int Height);
public sealed class MacroAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public ActionKind Kind { get; set; }
    public double DelayMs { get; set; }
    public double DurationMs { get; set; }
    public CoordinateMode Coordinates { get; set; }
    public WindowTarget? Target { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public List<PathPoint> Path { get; set; } = [];
    public MouseButton Button { get; set; }
    public bool IsDown { get; set; }
    public int WheelDelta { get; set; }
    public bool Horizontal { get; set; }
    public int VirtualKey { get; set; }
    public int ScanCode { get; set; }
    public bool Extended { get; set; }
    public List<int> Keys { get; set; } = [];
    public string Text { get; set; } = "";
    public string Comment { get; set; } = "";
    public DetectionSettings Detection { get; set; } = new();
    public DataSettings Data { get; set; } = new();
    public Dictionary<string,string> Bindings { get; set; } = [];
    public FlowSettings Flow {get;set;} = new();
    public bool Breakpoint {get;set;}
    public string Group {get;set;}="";
    [JsonIgnore] public string Description => Kind switch {
        ActionKind.MousePath => $"Move along {Path.Count} points · {DurationMs:0} ms",
        ActionKind.MouseButton => $"{Button} {(IsDown ? "down" : "up")} at {X}, {Y}",
        ActionKind.Scroll => $"{(Horizontal ? "Horizontal" : "Vertical")} scroll {WheelDelta} at {X}, {Y}",
        ActionKind.Key => $"Key 0x{VirtualKey:X2} {(IsDown ? "down" : "up")}",
        ActionKind.KeyChord => "Chord " + string.Join(" + ", Keys.Select(k => $"0x{k:X2}")),
        ActionKind.Text => $"Type “{Text.Replace("\n", " ↵ ")}”",
        ActionKind.Delay => $"Wait {DurationMs:0} ms",
        ActionKind.FocusWindow => $"Focus {Target?.Title ?? "window"}",
        >= ActionKind.Label => $"{Kind} · {Flow.Label}{Flow.Destination}{Flow.MacroFile}",
        >= ActionKind.Screenshot => $"{Kind} · {Data.Variable} · {Data.Value}",
        _ => $"{Kind} · {System.IO.Path.GetFileName(Detection.Image)} · {Detection.TimeoutMs:0} ms timeout"
    };
    [JsonIgnore] public string TargetLabel => Target is null ? "Desktop" : $"{Target.ProcessName} · {Target.Title}";
}
public sealed class PlaybackOptions
{
    public double Speed { get; set; } = 1;
    public int Repetitions { get; set; } = 1;
    public bool UntilStopped { get; set; }
    public bool SkipMousePaths { get; set; }
    public bool RestoreWindows { get; set; }
    public MousePathShape PathShape {get;set;}
}
public enum MousePathShape { Recorded, Linear, Curved }
public sealed class MacroDocument
{
    public int FormatVersion { get; set; } = 2;
    public string Name { get; set; } = "Untitled macro";
    public List<MacroAction> Actions { get; set; } = [];
    public PlaybackOptions Playback { get; set; } = new();
}
