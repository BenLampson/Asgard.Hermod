using Microsoft.AspNetCore.Authorization;

namespace Asgard.Hermod.Server.Controllers;

/// <summary>明细接口不接受归属标识或同步密码。</summary>
[Authorize]
[Route("api/sync/configuration")]
public sealed class ConfigurationSyncController(AbsAsgardContext context, WorkspaceSyncApplicationService service) : BaseController(context)
{
    /// <summary>读取当前用户的逐条配置和版本。</summary>
    [HttpGet]
    [ProducesResponseType<Response<CloudConfiguration>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<Response<CloudConfiguration>>> Get(CancellationToken cancellationToken) =>
        Success(await service.ReadConfigurationAsync(cancellationToken), Text.Get("ApiSuccess"));
    /// <summary>只有版本一致才提交全部合并结果。</summary>
    [HttpPut]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [ProducesResponseType<Response<CloudConfiguration>>(StatusCodes.Status200OK)]
    [ProducesResponseType<Response<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Response<CloudConfiguration>>> Put([FromBody] ConfigurationUpload upload, CancellationToken cancellationToken) =>
        Success(await service.UploadConfigurationAsync(upload, cancellationToken), Text.Get("ApiSuccess"));
}
