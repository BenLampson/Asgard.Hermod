namespace Asgard.Hermod.Abstractions.Models;

/// <summary>已验证的登录身份；用户名只用于展示，归属使用稳定主体标识。</summary>
public sealed record HermodAccount(string SubjectId, string TenantId, string ApplicationId, string Username);
