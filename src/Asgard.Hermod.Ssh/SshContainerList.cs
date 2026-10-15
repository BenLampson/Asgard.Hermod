namespace Asgard.Hermod.Ssh;

/// <summary>容器列表读取结果，失败状态不携带原始 SDK 或 stderr 错误。</summary>
public sealed record SshContainerList(SshContainerAvailability Availability, IReadOnlyList<SshContainerSnapshot> Items);
