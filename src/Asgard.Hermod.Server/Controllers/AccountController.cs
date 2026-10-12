using Microsoft.AspNetCore.Authorization;

namespace Asgard.Hermod.Server.Controllers;

/// <summary>当前用户基础 API，只返回本业务需要的稳定身份与 JWT 用户名。</summary>
[Authorize]
[Route("api/me")]
public sealed class AccountController(AbsAsgardContext context, WorkspaceSyncApplicationService service) : BaseController(context)
{
    /// <summary>通过服务端在线权限校验后返回当前用户。</summary>
    [HttpGet]
    [ProducesResponseType<Response<HermodAccount>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<Response<HermodAccount>>> Get(CancellationToken cancellationToken) =>
        Success(await service.GetAccountAsync(cancellationToken), Text.Get("ApiSuccess"));
}
