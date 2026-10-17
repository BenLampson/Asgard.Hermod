namespace Asgard.Hermod.Sync.Tests;

/// <summary>只模拟上游注册边界，不保存或输出注册密码。</summary>
internal sealed class TestAccountRegistrationService : IAccountRegistrationService
{
    /// <summary>模拟明确业务拒绝或意外故障。</summary>
    public Exception? Error { get; set; }
    /// <summary>统计真正进入服务的请求。</summary>
    public int Calls { get; private set; }
    /// <inheritdoc />
    public Task<AccountRegistrationResult> RegisterAsync(AccountRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        Calls++; request.Validate();
        if (Error is not null) throw Error;
        return Task.FromResult(new AccountRegistrationResult(request.Username.Trim()));
    }
}
