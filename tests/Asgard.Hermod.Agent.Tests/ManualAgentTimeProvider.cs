namespace Asgard.Hermod.Agent.Tests;

/// <summary>只推进受测超时计时器，不依赖系统时间或真实等待。</summary>
internal sealed class ManualAgentTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private TimeSpan _now;
    private readonly object _gate = new();
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            var timer = new ManualTimer(this, callback, state); _timers.Add(timer); timer.Change(dueTime, period); return timer;
        }
    }
    internal void Advance(TimeSpan elapsed)
    {
        ManualTimer[] due;
        lock (_gate) { _now += elapsed; due = _timers.Where(timer => timer.Due <= _now).ToArray(); foreach (var timer in due) timer.Dispose(); }
        foreach (var timer in due) timer.Callback(timer.State);
    }
    private sealed class ManualTimer(ManualAgentTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        internal TimerCallback Callback { get; } = callback;
        internal object? State { get; } = state;
        internal TimeSpan Due { get; private set; }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate) { Due = dueTime == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : owner._now + dueTime; return true; }
        }
        public void Dispose() { lock (owner._gate) owner._timers.Remove(this); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
