namespace Asgard.Hermod.UI.Models;

/// <summary>用户明确确认的移除决定；非当前目标不允许附带隐式切库。</summary>
public sealed record DatabaseRemovalDecision(string? ReplacementTarget = null);
