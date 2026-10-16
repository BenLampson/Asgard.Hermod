using System.Security.Authentication;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Abstractions.Models;

namespace Asgard.Hermod.PostgreSql;

/// <summary>只公开稳定 SQLSTATE 和资源键，不公开 SQL、服务器详情或凭据。</summary>
public sealed class PostgreSqlFailure(string key, string code, params object?[] arguments) : Exception(Text.Get(key)), ILocalizedError
{
    /// <summary>失败语句在本次执行文本中的位置；没有协议位置时只标记语句起点。</summary>
    public SqlErrorLocation? Location { get; private init; }
    private int? ServerPosition { get; init; }
    /// <summary>仅将当前执行范围和经过验证的协议数字映射为位置，不解析服务器消息原文。</summary>
    public PostgreSqlFailure AtStatement(string document, int start, int length, int statement) => new(ResourceKey, Code, ResourceArguments)
    { ServerPosition = ServerPosition, Location = SqlErrorLocation.InStatement(document, start, length, statement, ServerPosition) };
    /// <summary>供资源渲染的稳定错误键。</summary>
    public string ResourceKey { get; } = key;
    /// <summary>经过筛选的安全参数。</summary>
    public object?[] ResourceArguments { get; } = arguments;
    /// <summary>供业务判断的稳定码。</summary>
    public string Code { get; } = code;
    /// <summary>网络写入结果未知时禁止自动重放，服务器明确拒绝则保留其 SQLSTATE。</summary>
    public static PostgreSqlFailure Safe(Exception error, bool dispatchedWrite = false)
    {
        if (error is PostgreSqlFailure safe) return safe;
        if (error is Asgard.Hermod.Ssh.SshException) return new("PostgreSqlSshFailed", "POSTGRESQL_SSH");
        if (error is PostgresException postgres)
        {
            if (postgres.SqlState == "42501") return new("PostgreSqlPermissionDenied", "POSTGRESQL_PERMISSION");
            if (postgres.SqlState.StartsWith("28", StringComparison.Ordinal)) return new("PostgreSqlAuthenticationFailed", "POSTGRESQL_AUTH");
            if (postgres.SqlState == "3D000") return new("PostgreSqlDatabaseUnavailable", "POSTGRESQL_DATABASE");
            if (postgres.SqlState == "57014") return new("PostgreSqlQueryCancelled", "POSTGRESQL_CANCELLED");
            return new("PostgreSqlStatementRejected", "POSTGRESQL_STATEMENT", postgres.SqlState) { ServerPosition = postgres.Position > 0 ? postgres.Position : null };
        }
        if (error is AuthenticationException || error.InnerException is AuthenticationException) return new("PostgreSqlTlsFailed", "POSTGRESQL_TLS");
        return dispatchedWrite ? new("PostgreSqlSaveOutcomeUnknown", "POSTGRESQL_WRITE_UNKNOWN") : new("PostgreSqlCommandFailed", "POSTGRESQL_COMMAND");
    }
}
