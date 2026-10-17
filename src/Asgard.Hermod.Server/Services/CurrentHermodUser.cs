using Asgard.Abstractions.Identity;

namespace Asgard.Hermod.Server;

/// <summary>唯一身份边界来自宿主验签后的 Asgard 上下文，不接受请求提供所有者。</summary>
public sealed class CurrentHermodUser(AbsAsgardContext context, IHttpContextAccessor http, IOptions<HermodServerOptions> options)
{
    /// <summary>固定应用、租户和客户端；仅用户名展示读取 name 声明。</summary>
    public HermodAccount Get()
    {
        var identity = context.IdentityContext;
        var user = identity?.UserInfo;
        var expected = options.Value;
        if (identity?.TokenType != TokenType.UserLogin || user is null || string.IsNullOrWhiteSpace(user.Sub)
            || user.UserId != user.Sub || user.TenantId != expected.TenantId || user.ApplicationId != expected.ApplicationId
            || user.ClientId != expected.DesktopClientId || !user.Scope.Contains("hermod.sync")
            || !user.Permissions.Contains("hermod.sync")) throw new HermodApiException(403, "SyncForbidden");
        string? username = http.HttpContext?.User.FindFirst("name")?.Value;
        if (string.IsNullOrWhiteSpace(username)) throw new HermodApiException(403, "LoginIdentityIncomplete");
        return new(user.Sub, user.TenantId, user.ApplicationId, username);
    }
}
