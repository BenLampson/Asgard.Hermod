using System.Data;
using Asgard.Hermod.Abstractions.Models;
using Xunit;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>使用真实 DbDataReader 形状验证字段目录映射，缺失默认值、空字符串与原始表达式不能混淆。</summary>
public sealed class ColumnMetadataTests
{
    [Theory]
    [InlineData("PRI", DatabaseColumnKeyKind.Primary)]
    [InlineData("UNI", DatabaseColumnKeyKind.Unique)]
    [InlineData("MUL", DatabaseColumnKeyKind.Indexed)]
    [InlineData("", DatabaseColumnKeyKind.None)]
    [InlineData("future", DatabaseColumnKeyKind.Unknown)]
    public void ReaderPreservesServerFactsAndCompositeKeyMembership(string key, DatabaseColumnKeyKind expected)
    {
        using var table = Table(); table.Rows.Add("Mixed_字段", "numeric(18,4)", (short)7, false, key, "0.0000", "STORED", "客户原始备注", "price * quantity");
        using var reader = table.CreateDataReader(); Assert.True(reader.Read()); var definition = PostgreSqlColumnMetadata.Read(reader);
        Assert.Equal(7, definition.Ordinal); Assert.Equal("numeric(18,4)", definition.DataType); Assert.False(definition.Nullable);
        Assert.Equal(expected, definition.KeyKind); Assert.Equal("0.0000", definition.DefaultValue); Assert.Equal("客户原始备注", definition.Comment);
        Assert.Equal("price * quantity", definition.GenerationExpression); Assert.Equal("STORED", definition.Extra);
    }
    [Fact]
    public void MissingDefaultAndEmptyStringStayDistinctAndUnknownNullabilityIsNotGuessed()
    {
        using var table = Table(); table.Rows.Add("a", "text", 1, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, "a + 1");
        table.Rows.Add("b", "text", 2, false, "", "", "", "", "");
        using var reader = table.CreateDataReader(); Assert.True(reader.Read()); var missing = PostgreSqlColumnMetadata.Read(reader);
        Assert.Null(missing.DefaultValue); Assert.Null(missing.Nullable); Assert.Equal(DatabaseColumnKeyKind.Unknown, missing.KeyKind); Assert.Equal("a + 1", missing.GenerationExpression);
        Assert.True(reader.Read()); var empty = PostgreSqlColumnMetadata.Read(reader); Assert.Equal("", empty.DefaultValue); Assert.Equal("", empty.Comment);
    }
    private static DataTable Table()
    {
        var table = new DataTable(); for (int index = 0; index < 9; index++) table.Columns.Add("field" + index, typeof(object)); return table;
    }
}
