using System.Security.Authentication;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.MySql;

/// <summary>只向 UI 传递稳定错误和允许公开的数字错误码。</summary>
public sealed class MySqlFailure(string key, string code, params object?[] arguments) : Exception(Text.Get(key)), ILocalizedError
{
    /// <summary>供共享资源渲染的稳定错误键。</summary>
    public string ResourceKey { get; } = key;
    /// <summary>仅包含允许公开的错误参数。</summary>
    public object?[] ResourceArguments { get; } = arguments;
    /// <summary>诊断使用的稳定错误码。</summary>
    public string Code { get; } = code;
    /// <summary>将第三方错误映射为脱敏资源，不显示原始消息。</summary>
    public static MySqlFailure Safe(Exception error, bool dispatchedWrite = false)
    {
        if (error is MySqlFailure safe) return safe;
        if (error is Asgard.Hermod.Ssh.SshException) return new("MySqlSshFailed", "MYSQL_SSH");
        if (error is MySqlException mysql)
        {
            if (mysql.Number is 1044 or 1142 or 1143 or 1227) return new("MySqlPermissionDenied", "MYSQL_PERMISSION");
            if (mysql.Number == 1045) return new("MySqlAuthenticationFailed", "MYSQL_AUTH");
            if (mysql.Number == 1049) return new("MySqlDatabaseUnavailable", "MYSQL_DATABASE");
            // 服务端明确拒绝的语句与网络失败不同，失败消息原文仍不展示。
            if (mysql.Number is 1064 or 1054 or 1146 or 1062 or 1406 or 1366 or 1292 or 1048 or 1451 or 1452 or 1213)
                return new("MySqlStatementRejected", "MYSQL_STATEMENT", mysql.Number);
            if (!dispatchedWrite && mysql.Number is 1317 or 3024 or -1) return new("MySqlQueryCancelled", "MYSQL_CANCELLED");
        }
        if (error is AuthenticationException || error.InnerException is AuthenticationException) return new("MySqlTlsFailed", "MYSQL_TLS");
        return dispatchedWrite ? new("MySqlSaveOutcomeUnknown", "MYSQL_WRITE_UNKNOWN") : new("MySqlCommandFailed", "MYSQL_COMMAND");
    }
}
