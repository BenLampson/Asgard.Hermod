namespace Asgard.Hermod.Sync.Tests;

/// <summary>校准不能无限容忍错误系统时钟，也不能采用耗时过长的响应。</summary>
public sealed class HeimdallServerClockTests
{
    /// <summary>受信 HTTPS Date 校准八秒漂移，不改变系统时钟。</summary>
    [Fact]
    public void Observe_CorrectsSmallDriftAndRejectsLargeDrift()
    {
        var clock = new HeimdallServerClock(); var now = DateTimeOffset.UtcNow;
        clock.Observe(now.AddSeconds(8), TimeSpan.FromMilliseconds(100));
        Assert.InRange((clock.GetUtcNow() - DateTimeOffset.UtcNow).TotalSeconds, 8, 10);
        var error = Assert.Throws<InvalidOperationException>(() => clock.Observe(now.AddMinutes(10), TimeSpan.FromMilliseconds(100)));
        Assert.Equal("SystemClockOutOfSync", error.Data["Hermod.ResourceKey"]);
    }
    /// <summary>过长请求的 Date 无法可靠代表当前服务器时间，保留原时钟。</summary>
    [Fact]
    public void Observe_IgnoresSlowResponse()
    {
        var clock = new HeimdallServerClock(); clock.Observe(DateTimeOffset.UtcNow.AddMinutes(1), TimeSpan.FromSeconds(20));
        Assert.InRange((clock.GetUtcNow() - DateTimeOffset.UtcNow).TotalSeconds, -1, 1);
    }
}
