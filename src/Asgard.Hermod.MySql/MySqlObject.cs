namespace Asgard.Hermod.MySql;

/// <summary>对象原名保留协议大小写；分类标题由界面翻译。</summary>
public sealed record MySqlObject(MySqlObjectKind Kind, string Name, string Database = "", string ParentName = "", string Detail = "");

/// <summary>每个分支单独分页，失败不能伪装成空集合。</summary>
public sealed record MySqlObjectPage(IReadOnlyList<MySqlObject> Items, bool HasMore);
