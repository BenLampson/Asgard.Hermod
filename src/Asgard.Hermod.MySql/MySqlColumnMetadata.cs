using System.Data.Common;

namespace Asgard.Hermod.MySql;

/// <summary>字段目录只读取服务器原始列，不解析已经本地化的详情文本。</summary>
internal static class MySqlColumnMetadata
{
    internal const string Sql = "SELECT COLUMN_NAME, COLUMN_TYPE, ORDINAL_POSITION, IS_NULLABLE, COLUMN_KEY, COLUMN_DEFAULT, EXTRA, COLUMN_COMMENT, GENERATION_EXPRESSION " +
        "FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=@db AND TABLE_NAME=@parent ORDER BY ORDINAL_POSITION";
    internal static DatabaseColumnDefinition Read(DbDataReader reader) => new(Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture), reader.GetString(1),
        reader.IsDBNull(3) ? null : reader.GetString(3) switch { "YES" => true, "NO" => false, _ => null },
        reader.IsDBNull(4) ? DatabaseColumnKeyKind.Unknown : reader.GetString(4) switch { "PRI" => DatabaseColumnKeyKind.Primary, "UNI" => DatabaseColumnKeyKind.Unique, "MUL" => DatabaseColumnKeyKind.Indexed, "" => DatabaseColumnKeyKind.None, _ => DatabaseColumnKeyKind.Unknown },
        reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? "" : reader.GetString(6), reader.IsDBNull(7) ? "" : reader.GetString(7))
    { GenerationExpression = reader.IsDBNull(8) ? "" : reader.GetString(8) };
}
