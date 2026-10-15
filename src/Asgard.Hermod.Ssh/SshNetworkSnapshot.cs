namespace Asgard.Hermod.Ssh;

/// <summary>单接口计数、可空差分速率与地址；缺少 ip 工具时仍能读取 /proc 计数。</summary>
public sealed record SshNetworkSnapshot(string Name, string State, string Addresses, int? Mtu, long SentBytes, long ReceivedBytes,
    double? SentPerSecond, double? ReceivedPerSecond);
