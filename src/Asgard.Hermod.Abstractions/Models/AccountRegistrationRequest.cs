using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Asgard.Hermod.Abstractions.Models;

/// <summary>普通账号注册资料；不接受客户端指定身份归属、角色或审计字段。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AccountRegistrationRequest
{
    /// <summary>当前租户内唯一的登录名。</summary>
    public string Username { get; init; } = "";
    /// <summary>当前租户内唯一的登录邮箱。</summary>
    public string Email { get; init; } = "";
    /// <summary>仅为本次 TLS 注册请求保留的密码，不写入 Hermod 本地或同步存储。</summary>
    public string Password { get; init; } = "";

    /// <summary>客户端与服务端共享输入边界；密码保留原始空格，不做规范化。</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Username) || Username.Length > 255 || Username.Any(char.IsControl))
            throw Text.Exception("RegistrationInvalidUsername");
        if (string.IsNullOrWhiteSpace(Email) || Email.Length > 255 || !new EmailAddressAttribute().IsValid(Email.Trim()))
            throw Text.Exception("RegistrationInvalidEmail");
        if (string.IsNullOrWhiteSpace(Password) || Password.Length is < 12 or > 128)
            throw Text.Exception("RegistrationInvalidPassword");
    }
}
