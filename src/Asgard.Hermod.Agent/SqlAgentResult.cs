namespace Asgard.Hermod.Agent;

/// <summary>仅含安全状态和有界结果样本，未知结果不得自动重放。</summary>
public sealed record SqlAgentResult(string Status, string Output, bool StopActions = false);
