namespace Asgard.Hermod.Abstractions.Services;

/// <summary>由桌面用户确认外部 MCP 客户端要访问的实例和具体位置。</summary>
public interface IMcpApprovalService
{
    /// <summary>请求被取消、会话撤销或窗口关闭时不得返回已批准。</summary>
    Task<McpApprovalResult> ConfirmAsync(McpApprovalRequest request, CancellationToken cancellationToken = default);
}
