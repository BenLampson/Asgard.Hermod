namespace Asgard.Hermod.Ssh;

/// <summary>容器 ID 为后续日志请求的唯一标识；用户定义的名称和镜像不参与 shell 拼接。</summary>
public sealed record SshContainerSnapshot(string Id, string Name, string Image, string State, string Health, string Ports);
