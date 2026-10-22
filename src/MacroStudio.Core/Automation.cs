namespace MacroStudio.Core;

public sealed record ScreenRegion(int X, int Y, int Width, int Height);
public sealed class DetectionSettings
{
    public string Image { get; set; } = "";
    public ScreenRegion? Region { get; set; }
    public double Threshold { get; set; } = .94;
    public int TimeoutMs { get; set; } = 5000;
    public int PollMs { get; set; } = 100;
    public double TemplateDpi { get; set; } = 96;
    public double Scale { get; set; } = 1;
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public bool CoordinateFallback { get; set; }
    public string Color { get; set; } = "#FFFFFF";
    public int ColorTolerance { get; set; } = 8;
    public double ChangeFraction { get; set; } = .02;
}
public sealed class AutomationContext
{
    public string BaseDirectory { get; set; } = Environment.CurrentDirectory;
    public Func<CancellationToken, Task> Checkpoint { get; set; } = t => { t.ThrowIfCancellationRequested(); return Task.CompletedTask; };
    public VariableStore Variables { get; } = new();
    public string ResolvePath(string path) => Path.GetFullPath(Variables.Substitute(path), BaseDirectory);
}
public interface IAutomationHost
{
    Task ValidateAsync(MacroProgram program,CancellationToken token) => Task.CompletedTask;
    Task ExecuteAsync(MacroAction action, AutomationContext context, CancellationToken token);
}
