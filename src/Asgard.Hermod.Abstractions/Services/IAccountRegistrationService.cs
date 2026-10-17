namespace Asgard.Hermod.Abstractions.Services;

/// <summary>创建普通用户；注册不提交登录会话，也不自动重复发送创建请求。</summary>
public interface IAccountRegistrationService
{
    /// <summary>通过可信服务端创建账号，归属和普通权限由服务端身份系统决定。</summary>
    Task<AccountRegistrationResult> RegisterAsync(AccountRegistrationRequest request, CancellationToken cancellationToken = default);
}
