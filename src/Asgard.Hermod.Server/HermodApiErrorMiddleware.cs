using Microsoft.AspNetCore.Builder;

namespace Asgard.Hermod.Server;

/// <summary>API 统一失败包络不包含原始异常、SDK 响应或凭据。</summary>
public sealed class HermodApiErrorMiddleware(RequestDelegate next)
{
    /// <summary>保持错误码，给客户端安全本地化消息；目录不可确认时返回服务不可用。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error) when (!context.Response.HasStarted)
        {
            int status = error is HermodApiException business ? business.StatusCode
                : error is BadHttpRequestException requestError ? requestError.StatusCode
                : error is HeimdallClientException or HttpRequestException ? 503
                : error.Data["Hermod.ResourceKey"] is not null ? 400 : 500;
            string message = error is HermodApiException known ? Text.Get(known.ResourceKey)
                : error is BadHttpRequestException ? Text.Get(context.Request.Path == "/api/account/register" ? "RegistrationInvalidRequest" : "SyncSnapshotInvalid")
                : Text.Error(error, "SyncRequestFailed");
            context.Response.Clear(); context.Response.StatusCode = status;
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.WriteAsJsonAsync(Response.Fail<object>(status, message), context.RequestAborted);
        }
    }
}
