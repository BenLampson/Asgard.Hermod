namespace Asgard.Hermod.Abstractions.Services;

/// <summary>保存当前设备核验过的主机身份，避免连接配置或同步记录替代身份核验。</summary>
public interface IHostKeyTrustStore
{
    /// <summary>按规范化主机和端口读取可信指纹。</summary>
    Task<TrustedHostKey?> LoadAsync(string host, int port, CancellationToken cancellationToken = default);
    /// <summary>只在旧指纹仍匹配时写入，首次信任的旧值为空；并发变化返回 false。</summary>
    Task<bool> TrustAsync(TrustedHostKey key, string? expectedFingerprint, CancellationToken cancellationToken = default);
}
