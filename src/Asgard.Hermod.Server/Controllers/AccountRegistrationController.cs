using Asgard.Hermod.Abstractions.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace Asgard.Hermod.Server.Controllers;

/// <summary>公共普通账号注册入口；只向 Heimdall 提交资料，不代用户登录。</summary>
[AllowAnonymous]
[Route("api/account/register")]
[EnableRateLimiting("hermod-registration")]
public sealed class AccountRegistrationController(AbsAsgardContext context, IAccountRegistrationService service) : BaseController(context)
{
    /// <summary>限制请求大小和创建频率，返回不含凭据的注册确认。</summary>
    [HttpPost]
    [RequestSizeLimit(8192)]
    [ProducesResponseType<Response<AccountRegistrationResult>>(StatusCodes.Status200OK)]
    [ProducesResponseType<Response<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Response<object>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<Response<object>>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<Response<AccountRegistrationResult>>> Register([FromBody] AccountRegistrationRequest request, CancellationToken cancellationToken) =>
        Success(await service.RegisterAsync(request, cancellationToken), Text.Get("RegistrationSucceeded"));
}
