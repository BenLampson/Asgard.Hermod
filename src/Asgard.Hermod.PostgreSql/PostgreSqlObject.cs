namespace Asgard.Hermod.PostgreSql;

/// <summary>对象原名保留协议大小写；分类标题由界面翻译。</summary>
public sealed record PostgreSqlObject(PostgreSqlObjectKind Kind, string Name, string Database = "", string ParentName = "", string Detail = "", string Schema = "");
