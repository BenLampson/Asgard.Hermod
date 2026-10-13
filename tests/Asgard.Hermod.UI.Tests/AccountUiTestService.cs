namespace Asgard.Hermod.UI.Tests;

/// <summary>窗口测试的账户与同步边界，不访问浏览器或网络。</summary>
internal sealed class AccountUiTestService : IAccountService, IWorkspaceSyncService
{
    /// <inheritdoc />
    public HermodAccount? Account { get; private set; }
    /// <summary>测试不同长度与字符的 JWT 用户名，不伪造显示名称或邮箱。</summary>
    public string Username { get; set; } = "JWT-用户名";
    /// <inheritdoc />
    public event EventHandler? AccountChanged;
    /// <summary>记录实际上传次数。</summary>
    public int Uploads { get; private set; }
    /// <summary>记录实际恢复次数。</summary>
    public int Downloads { get; private set; }
    /// <summary>模拟异步同步边界的失败、取消和重试，不访问真实云端。</summary>
    public Func<string, CancellationToken, Task>? SyncAction { get; set; }
    /// <inheritdoc />
    public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    /// <inheritdoc />
    public Task SignInAsync(CancellationToken cancellationToken = default)
    {
        Account = new("sub", "tenant", "app", Username); AccountChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask;
    }
    /// <inheritdoc />
    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        Account = null; AccountChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask;
    }
    /// <inheritdoc />
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("test");
    /// <inheritdoc />
    public Task RunAuthenticatedAsync(Func<HermodAccount, string, Task> action, CancellationToken cancellationToken = default) => action(Account!, "test");
    /// <inheritdoc />
    public Task UploadAsync(string password, CancellationToken cancellationToken = default)
    { Uploads++; return SyncAction?.Invoke(password, cancellationToken) ?? Task.CompletedTask; }
    /// <inheritdoc />
    public Task DownloadAsync(string password, CancellationToken cancellationToken = default)
    { Downloads++; return SyncAction?.Invoke(password, cancellationToken) ?? Task.CompletedTask; }
}
