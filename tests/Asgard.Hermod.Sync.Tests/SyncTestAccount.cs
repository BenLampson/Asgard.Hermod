namespace Asgard.Hermod.Sync.Tests;

/// <summary>自动同步测试的已验证账户边界。</summary>
internal sealed class SyncTestAccount(string tenant, string application) : IAccountService
{
    public HermodAccount? Account { get; private set; } = new("user-a", tenant, application, "User A");
    public event EventHandler? AccountChanged;
    public string Token { get; set; } = "";
    /// <summary>模拟用户主动切换到新身份。</summary>
    public void Change(string? subject)
    {
        Account = subject is null ? null : new(subject, tenant, application, subject); AccountChanged?.Invoke(this, EventArgs.Empty);
    }
    public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SignInAsync(CancellationToken cancellationToken = default) { Change("user-a"); return Task.CompletedTask; }
    public Task SignOutAsync(CancellationToken cancellationToken = default) { Change(null); return Task.CompletedTask; }
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(Token);
    public Task RunAuthenticatedAsync(Func<HermodAccount, string, Task> action, CancellationToken cancellationToken = default) => action(Account!, Token);
    public Task RunWithAccountAsync(Func<HermodAccount?, Task> action, CancellationToken cancellationToken = default) => action(Account);
}
