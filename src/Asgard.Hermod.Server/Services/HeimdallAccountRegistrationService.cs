using System.Net;
using Asgard.Hermod.Abstractions.Services;

namespace Asgard.Hermod.Server.Services;

/// <summary>独立 SDK 客户端只申请用户供给权限，不复用用户登录或目录访问令牌。</summary>
public sealed class HeimdallAccountRegistrationService(HeimdallClientFactory clients, HeimdallTokenProvider tokens) : IAccountRegistrationService
{
    /// <inheritdoc />
    public async Task<AccountRegistrationResult> RegisterAsync(AccountRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        try
        {
            // 先确认服务授权，避免把 Token Endpoint 的 invalid_scope / invalid_client 误报成用户资料错误。
            // SDK 按同一命名配置与凭据版本缓存验证后的令牌，后面的创建不会平行实现兑换逻辑。
            _ = await tokens.GetAccessTokenAsync("hermod-registration", cancellationToken);
        }
        catch (Exception error) when (error is HeimdallClientException or HttpRequestException
            || error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new HermodApiException(503, "RegistrationUnavailable");
        }
        try
        {
            using var client = clients.CreateUserProvisioning("hermod-registration");
            var created = await client.CreateUserAsync(new HeimdallUserCreateRequest
            {
                Username = request.Username.Trim(),
                Email = request.Email.Trim(),
                Password = request.Password,
            }, cancellationToken);
            return new(created.Username);
        }
        catch (HeimdallClientException error)
        {
            // 不传播 SDK 正文；明确拒绝与结果未知分开，避免用户盲目重复创建。
            throw error.Status switch
            {
                HttpStatusCode.BadRequest => new HermodApiException(400, "RegistrationInvalidRequest"),
                HttpStatusCode.Conflict => new HermodApiException(409, "RegistrationConflict"),
                HttpStatusCode.TooManyRequests => new HermodApiException(429, "RegistrationRateLimited"),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new HermodApiException(503, "RegistrationUnavailable"),
                _ => new HermodApiException(502, "RegistrationOutcomeUnknown"),
            };
        }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new HermodApiException(502, "RegistrationOutcomeUnknown");
        }
    }
}
