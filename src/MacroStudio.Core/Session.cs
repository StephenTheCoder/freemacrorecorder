using System.Diagnostics;

namespace MacroStudio.Core;
public enum SessionState { Idle, Recording, Playing, Paused, Stopping }
public sealed class SessionMachine
{
    readonly object gate = new();
    SessionState state;
    public SessionState State { get { lock (gate) return state; } }
    public event Action<SessionState>? Changed;
    public bool Transition(SessionState next) {
        lock (gate) {
            bool allowed = (state, next) switch {
                (SessionState.Idle, SessionState.Recording or SessionState.Playing) => true,
                (SessionState.Recording, SessionState.Stopping) => true,
                (SessionState.Playing, SessionState.Paused or SessionState.Stopping) => true,
                (SessionState.Paused, SessionState.Playing or SessionState.Stopping) => true,
                (SessionState.Stopping, SessionState.Idle) => true, _ => false
            };
            if (!allowed) return false; state = next;
        }
        Changed?.Invoke(next); return true;
    }
}
public sealed class PlaybackClock
{
    readonly Stopwatch stopwatch = Stopwatch.StartNew();
    readonly object gate = new();
    double pausedAt = -1, pausedTotal;
    public double Elapsed { get { lock(gate) return (pausedAt >= 0 ? pausedAt : stopwatch.Elapsed.TotalMilliseconds) - pausedTotal; } }
    public bool Paused { get { lock(gate) return pausedAt >= 0; } }
    public void Pause() { lock(gate) { if (pausedAt < 0) pausedAt = stopwatch.Elapsed.TotalMilliseconds; } }
    public void Resume() { lock(gate) { if (pausedAt >= 0) { pausedTotal += stopwatch.Elapsed.TotalMilliseconds - pausedAt; pausedAt = -1; } } }
    public async Task WaitUntilAsync(double deadline, CancellationToken token) {
        while (true) { token.ThrowIfCancellationRequested(); var remaining = deadline - Elapsed; if (!Paused && remaining <= 0) return; await Task.Delay(Paused ? 10 : (int)Math.Clamp(Math.Ceiling(remaining), 1, 10), token); }
    }
    public static double Scale(double ms, double speed) => ms / speed;
}
public interface IInputSink
{
    void Move(int x, int y, MacroAction action);
    void Button(MouseButton button, bool down);
    void Scroll(int delta, bool horizontal);
    void Key(int vk, int scan, bool extended, bool down);
    void Unicode(char c, bool down);
    void Focus(WindowTarget target, bool restore);
    void ReleaseAll();
}
public sealed class PlaybackEngine(IInputSink sink, SessionMachine session)
{
    readonly object controlGate = new();
    CancellationTokenSource? cancellation;
    PlaybackClock? clock;
    public event Action<Guid, int>? Progress;
    public void Stop() { lock(controlGate) { if (session.State is SessionState.Playing or SessionState.Paused) session.Transition(SessionState.Stopping); cancellation?.Cancel(); } }
    public void TogglePause() {
        lock(controlGate) {
            if (session.State == SessionState.Playing) { clock?.Pause(); session.Transition(SessionState.Paused); }
            else if (session.State == SessionState.Paused) { clock?.Resume(); session.Transition(SessionState.Playing); }
        }
    }
    public async Task PlayAsync(IEnumerable<MacroAction> source, PlaybackOptions options, CancellationToken token = default, double startDelayMs = 0)
    {
        var actions = source.Select(MacroStore.Clone).ToList();
        MacroStore.Validate(new() { Actions = actions, Playback = options });
        if (!actions.Any(a => a.Enabled)) throw new InvalidOperationException("There are no enabled actions to play.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock(controlGate) {
            if(session.State != SessionState.Idle) throw new InvalidOperationException("Stop the current session first.");
            cancellation = cts; clock = new();
            if (!session.Transition(SessionState.Playing)) { cancellation=null;clock=null;throw new InvalidOperationException("Stop the current session first."); }
        }
        try {
            double deadline = Math.Max(0,startDelayMs);
            for (int repetition = 1; options.UntilStopped || repetition <= options.Repetitions; repetition++) {
                foreach (var a in actions.Where(a => a.Enabled)) {
                    cts.Token.ThrowIfCancellationRequested();
                    deadline += PlaybackClock.Scale(a.DelayMs, options.Speed);
                    await clock.WaitUntilAsync(deadline, cts.Token);
                    Progress?.Invoke(a.Id, repetition);
                    if (a.Kind == ActionKind.MousePath) {
                        foreach(var p in a.Path) { await clock.WaitUntilAsync(deadline + PlaybackClock.Scale(p.OffsetMs, options.Speed), cts.Token); if (!options.SkipMousePaths) sink.Move(p.X, p.Y, a); }
                        deadline += PlaybackClock.Scale(a.DurationMs, options.Speed);
                    } else if (a.Kind == ActionKind.Delay) deadline += PlaybackClock.Scale(a.DurationMs, options.Speed);
                    else switch(a.Kind) {
                        case ActionKind.MouseButton: sink.Move(a.X,a.Y,a); sink.Button(a.Button,a.IsDown); break;
                        case ActionKind.Scroll: sink.Move(a.X,a.Y,a); sink.Scroll(a.WheelDelta,a.Horizontal); break;
                        case ActionKind.Key: sink.Key(a.VirtualKey,a.ScanCode,a.Extended,a.IsDown); break;
                        case ActionKind.KeyChord:
                            foreach(var key in a.Keys) { cts.Token.ThrowIfCancellationRequested(); sink.Key(key,0,false,true); }
                            foreach(var key in a.Keys.AsEnumerable().Reverse()) sink.Key(key,0,false,false); break;
                        case ActionKind.Text:
                            foreach(var c in a.Text) { cts.Token.ThrowIfCancellationRequested(); await clock.WaitUntilAsync(deadline,cts.Token); sink.Unicode(c,true); sink.Unicode(c,false); } break;
                        case ActionKind.FocusWindow: sink.Focus(a.Target!, options.RestoreWindows); break;
                    }
                    await clock.WaitUntilAsync(deadline, cts.Token);
                }
                sink.ReleaseAll();
                // Bound zero-duration loops and give foreground hotkey messages time to run.
                await Task.Delay(1, cts.Token);
                deadline = Math.Max(deadline, clock.Elapsed);
            }
        } finally {
            try { sink.ReleaseAll(); } finally { lock(controlGate) { cancellation = null; clock = null; if(session.State != SessionState.Stopping) session.Transition(SessionState.Stopping); session.Transition(SessionState.Idle); } }
        }
    }
}
