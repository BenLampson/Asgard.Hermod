namespace Asgard.Hermod.Abstractions.Models;

/// <summary>提供经过脱敏的测试状态，不传播第三方原始异常或请求内容。</summary>
public sealed record InstanceConnectionTestResult(bool IsSuccessful, string Message,
    string? ErrorCode = null, IReadOnlyList<InstanceConnectionCheck>? Checks = null)
{
    /// <summary>取消后仍需告知用户的失败，例如临时资源清理失败；不能用于恢复测试成功状态。</summary>
    public bool RequiresAttentionAfterCancellation { get; init; }
}
