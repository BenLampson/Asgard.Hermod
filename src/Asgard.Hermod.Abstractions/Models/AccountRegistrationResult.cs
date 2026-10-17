namespace Asgard.Hermod.Abstractions.Models;

/// <summary>注册确认只返回规范化登录名，不返回密码、服务凭据或用户令牌。</summary>
public sealed record AccountRegistrationResult(string Username);
