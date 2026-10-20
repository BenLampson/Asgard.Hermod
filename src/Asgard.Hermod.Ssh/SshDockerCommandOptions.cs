namespace Asgard.Hermod.Ssh;

/// <summary>表单的协议参数；容器使用完整 ID，Compose 路径必须显式指定。</summary>
public sealed record SshDockerCommandOptions(string ContainerId = "", int TailLines = 500, string Since = "1h",
    bool Timestamps = true, bool Follow = false, int TimeoutSeconds = 10, string Image = "",
    string ProjectDirectory = "", string ComposeFile = "compose.yaml", string Service = "");
