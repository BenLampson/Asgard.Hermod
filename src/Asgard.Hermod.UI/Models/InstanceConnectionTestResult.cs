namespace Asgard.Hermod.UI.Models;

/// <summary>提供经过脱敏的测试状态，不传播第三方原始异常或请求内容。</summary>
public sealed record InstanceConnectionTestResult(bool IsSuccessful, string Message,
    string? ErrorCode = null, IReadOnlyList<InstanceConnectionCheck>? Checks = null);
