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
