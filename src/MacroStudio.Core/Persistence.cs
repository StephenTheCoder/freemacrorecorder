using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroStudio.Core;

public static class MacroStore
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static string Serialize(MacroDocument doc) { Validate(doc); return JsonSerializer.Serialize(doc, Json); }
    public static MacroDocument Parse(string json)
    {
        try { var doc = JsonSerializer.Deserialize<MacroDocument>(json, Json) ?? throw new InvalidDataException("The macro is empty."); Validate(doc); return doc; }
        catch (JsonException ex) { throw new InvalidDataException($"Invalid macro JSON at {ex.Path}, line {ex.LineNumber + 1}: {ex.Message}", ex); }
    }
    public static MacroAction Clone(MacroAction a) => JsonSerializer.Deserialize<MacroAction>(JsonSerializer.Serialize(a, Json), Json)!;
    public static void Validate(MacroDocument d)
    {
        if (d.FormatVersion is < 1 or > 2) throw new InvalidDataException($"Unsupported macro format {d.FormatVersion}; this version supports 1 and 2.");
        if (d.Name is null || d.Actions is null || d.Playback is null || d.Actions.Count > 200000) throw new InvalidDataException("Missing document fields or too many actions.");
        if (!double.IsFinite(d.Playback.Speed) || d.Playback.Speed < .05 || d.Playback.Speed > 100 || d.Playback.Repetitions < 1 || d.Playback.Repetitions > 1000000) throw new InvalidDataException("Speed must be 0.05–100 and repetitions 1–1000000.");
        var ids = new HashSet<Guid>();
        foreach (var a in d.Actions) {
            if (a is null || a.Id == Guid.Empty || !ids.Add(a.Id)) throw new InvalidDataException("Actions must have unique, nonempty IDs.");
            if (!Enum.IsDefined(a.Kind) || !Enum.IsDefined(a.Coordinates) || !Enum.IsDefined(a.Button)) throw new InvalidDataException("Unknown action type or coordinate mode.");
            if (!ValidTime(a.DelayMs) || !ValidTime(a.DurationMs)) throw new InvalidDataException($"Invalid timing for action {a.Id} (allowed: 0–86400000 ms).");
            if (a.Path is null || a.Keys is null || a.Text is null || a.Comment is null || a.Text.Length > 1000000 || a.Path.Count > 1000000) throw new InvalidDataException("Missing or excessive action data.");
            if (a.VirtualKey < 0 || a.VirtualKey > 255 || a.ScanCode < 0 || a.ScanCode > 65535 || a.Keys.Any(k => k < 1 || k > 255)) throw new InvalidDataException("Key codes must be in the Windows virtual-key range.");
            if (a.Kind == ActionKind.Key && a.VirtualKey == 0 || a.Kind == ActionKind.KeyChord && a.Keys.Count == 0) throw new InvalidDataException("Key actions require a key code.");
            if ((a.Coordinates == CoordinateMode.Window || a.Kind == ActionKind.FocusWindow) && a.Target is null) throw new InvalidDataException("Window-relative and focus actions require a target.");
            if (a.Target is { } w && (string.IsNullOrWhiteSpace(w.ProcessName) || w.Title is null || w.Width < 1 || w.Height < 1)) throw new InvalidDataException("Invalid window target.");
            if (a.Detection is not {} v || !double.IsFinite(v.Threshold) || v.Threshold is < .6 or > 1 || v.TimeoutMs is < 1 or > 86400000 || v.PollMs is < 20 or > 5000 || !double.IsFinite(v.TemplateDpi) || v.TemplateDpi is < 48 or > 768 || v.Scale != 1 || v.ColorTolerance is < 0 or > 255 || !double.IsFinite(v.ChangeFraction) || v.ChangeFraction is <= 0 or > 1)
                throw new InvalidDataException("Invalid detection settings. Only exact physical-pixel scale 1 is supported; recapture templates after DPI/zoom changes.");
            if (v.Region is {} r && (r.Width < 1 || r.Height < 1 || (long)r.Width*r.Height > 16000000)) throw new InvalidDataException("Search/capture region must contain 1–16 million physical pixels.");
            if(a.Data is null||a.Bindings is null||!Enum.IsDefined(a.Data.Type)||a.Data.Row<0||a.Data.Column<0||a.Bindings.Keys.Any(k=>!VariableStore.AllowedBindings.Contains(k)))throw new InvalidDataException("Invalid data settings or property bindings.");
            if(a.Flow is null||a.Group is null||a.Flow.Count is <1 or >1000000||a.Flow.Retries is <0 or >100||!Enum.IsDefined(a.Flow.Timeout)||!Enum.IsDefined(d.Playback.PathShape))throw new InvalidDataException("Invalid flow settings: repeat count 1–1000000; retries 0–100.");
            if(v.Image is null||v.Color is null||a.Data.Variable is null||a.Data.Value is null||a.Data.Operand is null||a.Data.Operation is null||a.Data.File is null||a.Data.Language is null||a.Flow.Label is null||a.Flow.Destination is null||a.Flow.Otherwise is null||a.Flow.MacroFile is null||a.Bindings.Values.Any(value=>value is null||value.Length>10000))throw new InvalidDataException("Action settings and binding expressions cannot be null or excessive.");
            if(a.Kind==ActionKind.WaitPixel&&!v.Color.Contains("${")&&!System.Text.RegularExpressions.Regex.IsMatch(v.Color,"^#[0-9A-Fa-f]{6}$"))throw new InvalidDataException("Pixel color must use #RRGGBB.");
            if(a.Data.Value.Length>1000000||a.Data.Operand.Length>1000000||a.Comment.Length>1000000||a.Group.Length>256)throw new InvalidDataException("Excessive data, comment or group length.");
            double previous = -1;
            foreach (var p in a.Path) { if (p is null || !ValidTime(p.OffsetMs) || p.OffsetMs < previous || p.OffsetMs > a.DurationMs) throw new InvalidDataException("Path timestamps must be ordered and within its duration."); previous = p.OffsetMs; }
        }
    }
    static bool ValidTime(double n) => double.IsFinite(n) && n >= 0 && n <= 86400000;
    public static async Task<MacroDocument> LoadAsync(string path) {
        if (new FileInfo(path).Length > 100 * 1024 * 1024) throw new InvalidDataException("Macro exceeds the 100 MB file limit.");
        return Parse(await File.ReadAllTextAsync(path));
    }
    public static async Task SaveAsync(string path, MacroDocument document)
    {
        var json = Serialize(document);
        path = Path.GetFullPath(path);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) {
                await using var writer = new StreamWriter(stream); await writer.WriteAsync(json); await writer.FlushAsync(); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
