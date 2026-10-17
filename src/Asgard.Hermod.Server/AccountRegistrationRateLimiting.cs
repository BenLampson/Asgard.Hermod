using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Asgard.Hermod.Server;

/// <summary>注册使用独立的全局创建预算，不信任客户端伪造的转发地址，也不影响登录和同步。</summary>
public static class AccountRegistrationRateLimiting
{
    /// <summary>每分钟最多二十个注册请求，不排队；拒绝响应仍使用安全统一包络。</summary>
    public static IServiceCollection AddAccountRegistrationRateLimiting(this IServiceCollection services) => services.AddRateLimiter(options =>
    {
        options.AddFixedWindowLimiter("hermod-registration", limiter =>
        {
            limiter.PermitLimit = 20;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueLimit = 0;
        });
        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers.RetryAfter = "60";
            await context.HttpContext.Response.WriteAsJsonAsync(Response.Fail<object>(429, Text.Get("RegistrationRateLimited")), cancellationToken);
        };
    });
}
