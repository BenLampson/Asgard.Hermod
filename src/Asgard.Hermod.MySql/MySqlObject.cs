namespace Asgard.Hermod.MySql;

/// <summary>对象原名保留协议大小写；分类标题由界面翻译。</summary>
public sealed record MySqlObject(MySqlObjectKind Kind, string Name, string Database = "", string ParentName = "", string Detail = "");
