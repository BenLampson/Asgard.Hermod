namespace Asgard.Hermod.UI.Tests;

/// <summary>给签名请求提供固定 UTC 时间，避免测试依赖系统时钟。</summary>
public sealed class FixedStorageTestTimeProvider : TimeProvider
{
    /// <summary>测试约定的签名时间。</summary>
    public override DateTimeOffset GetUtcNow() => new(2025, 4, 11, 6, 41, 24, TimeSpan.Zero);
}
