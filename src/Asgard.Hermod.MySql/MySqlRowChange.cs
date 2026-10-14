namespace Asgard.Hermod.MySql;

/// <summary>一行的完整服务器基线和待写字段，不以 UI 行号定位数据。</summary>
public sealed record MySqlRowChange(IReadOnlyDictionary<string, object?> Original, IReadOnlyDictionary<string, object?> Changes);

/// <summary>冲突保留基线、最新服务器数据和草稿。</summary>
public sealed class MySqlConflict(MySqlRowChange change, IReadOnlyDictionary<string, object?>? current)
    : Exception(Text.Get("MySqlEditConflict")), Asgard.Hermod.Abstractions.Localization.ILocalizedError
{
    public MySqlRowChange Change { get; } = change;
    public IReadOnlyDictionary<string, object?>? Current { get; } = current;
    public string ResourceKey => "MySqlEditConflict";
    public object?[] ResourceArguments => [];
}
