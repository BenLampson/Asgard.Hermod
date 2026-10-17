namespace Asgard.Hermod.PostgreSql;

/// <summary>一行的完整服务器基线和待写字段，不以 UI 行号定位数据。</summary>
public sealed record PostgreSqlRowChange(IReadOnlyDictionary<string, object?> Original, IReadOnlyDictionary<string, object?> Changes);
