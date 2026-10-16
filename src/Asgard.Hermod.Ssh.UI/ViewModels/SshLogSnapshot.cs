namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>一次成功日志读取的不可变原始证据，选择器后续变化不改写本次范围。</summary>
public sealed record SshLogSnapshot(string DocumentId, string Host, string ContainerId, string ContainerName,
    string Source, string Text, DateTimeOffset CapturedAt, int RequestedLines, bool IsTruncated);
