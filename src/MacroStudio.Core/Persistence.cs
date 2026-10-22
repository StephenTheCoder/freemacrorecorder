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
        if (d.FormatVersion != 1) throw new InvalidDataException($"Unsupported macro format {d.FormatVersion}; this version supports 1.");
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
