namespace Asgard.Hermod.PostgreSql;

/// <summary>按序号区分同名结果列，编辑权限来自服务器元数据。</summary>
public sealed record PostgreSqlColumn(int Ordinal, string Name, string TypeName, Type ValueType, bool Nullable,
    string Database = "", string Table = "", string BaseName = "", bool PrimaryKey = false, bool CanUpdate = false, bool Generated = false, string Schema = "public");
