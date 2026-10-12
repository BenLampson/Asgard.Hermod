namespace Asgard.Hermod.Identity;

/// <summary>以固定可信发行者的 HTTPS Date 校准 SDK 时间，抵御桌面小幅时钟漂移。</summary>
public sealed class HeimdallServerClock(TimeProvider? localClock = null) : TimeProvider
{
    private readonly TimeProvider _local = localClock ?? TimeProvider.System;
    private long _offsetTicks;
    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _local.GetUtcNow().AddTicks(Interlocked.Read(ref _offsetTicks));
    /// <summary>HTTP Date 精度为秒；取下一秒避免合法 nbf 因舍入而落在未来，最多提前一秒到期。</summary>
    public void Observe(DateTimeOffset serverDate, TimeSpan requestDuration)
    {
        if (requestDuration > TimeSpan.FromSeconds(10)) return;
        TimeSpan offset = serverDate.AddSeconds(1) - _local.GetUtcNow();
        if (offset.Duration() > TimeSpan.FromMinutes(2)) throw Text.Exception("SystemClockOutOfSync");
        Interlocked.Exchange(ref _offsetTicks, offset.Ticks);
    }
}
