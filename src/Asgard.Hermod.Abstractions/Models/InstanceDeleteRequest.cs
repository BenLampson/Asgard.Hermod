namespace Asgard.Hermod.Abstractions.Models;

/// <summary>删除只影响打开编辑器时的工作区，不能在账户切换后误删另一份配置。</summary>
public sealed record InstanceDeleteRequest(HermodAccount? ExpectedAccount);
