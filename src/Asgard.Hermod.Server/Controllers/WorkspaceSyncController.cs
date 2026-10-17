using Microsoft.AspNetCore.Authorization;

namespace Asgard.Hermod.Server.Controllers;

/// <summary>不接受 tenantId、userId 或密码；所有数据归属来自验证后的用户 JWT。</summary>
[Authorize]
[Route("api/sync/workspace")]
public sealed class WorkspaceSyncController(AbsAsgardContext context, WorkspaceSyncApplicationService service) : BaseController(context)
{
    /// <summary>获取个人最新密文快照及版本。</summary>
    [HttpGet]
    [ProducesResponseType<Response<CloudWorkspace>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<Response<CloudWorkspace>>> Get(CancellationToken cancellationToken) =>
        Success(await service.ReadAsync(cancellationToken), Text.Get("ApiSuccess"));
    /// <summary>只有设备已知版本匹配才提交，过期上传返回 409。</summary>
    [HttpPut]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [ProducesResponseType<Response<CloudWorkspace>>(StatusCodes.Status200OK)]
    [ProducesResponseType<Response<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Response<CloudWorkspace>>> Put([FromBody] WorkspaceUpload upload, CancellationToken cancellationToken) =>
        Success(await service.UploadAsync(upload, cancellationToken), Text.Get("ApiSuccess"));
}
