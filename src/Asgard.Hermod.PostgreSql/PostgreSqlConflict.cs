namespace Asgard.Hermod.PostgreSql;

/// <summary>冲突保留基线、最新服务器数据和草稿。</summary>
public sealed class PostgreSqlConflict(PostgreSqlRowChange change, IReadOnlyDictionary<string, object?>? current)
    : Exception(Text.Get("PostgreSqlEditConflict")), Asgard.Hermod.Abstractions.Localization.ILocalizedError
{
    /// <summary>保留完整基线的本地行草稿。</summary>
    public PostgreSqlRowChange Change { get; } = change;
    /// <summary>冲突时读到的服务器行；删除后为空。</summary>
    public IReadOnlyDictionary<string, object?>? Current { get; } = current;
    /// <summary>供共享资源渲染的稳定错误键。</summary>
    public string ResourceKey => "PostgreSqlEditConflict";
    /// <summary>仅包含允许公开的错误参数。</summary>
    public object?[] ResourceArguments => [];
}
