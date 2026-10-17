namespace Asgard.Hermod.UI.Tests;

/// <summary>注册界面的服务边界，不保存密码或改变登录账号。</summary>
internal sealed class RegistrationUiTestService : IAccountRegistrationService
{
    /// <summary>模拟安全业务失败或未知故障。</summary>
    public Exception? Error { get; set; }
    /// <summary>实际提交次数。</summary>
    public int Calls { get; private set; }
    /// <inheritdoc />
    public Task<AccountRegistrationResult> RegisterAsync(AccountRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (Error is not null) throw Error;
        return Task.FromResult(new AccountRegistrationResult(request.Username.Trim()));
    }
}
