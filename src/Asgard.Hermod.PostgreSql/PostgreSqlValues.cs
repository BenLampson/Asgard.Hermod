using System.Text;
using System.Text.Json;

namespace Asgard.Hermod.PostgreSql;

/// <summary>类型转换使用 invariant；SQL NULL 与空字符串不同。</summary>
public static class PostgreSqlValues
{
    /// <summary>比较类型化值；二进制数据按字节比较。</summary>
    public static bool Equal(object? left, object? right) => left is byte[] a && right is byte[] b ? a.AsSpan().SequenceEqual(b) : Equals(left, right);
    /// <summary>复制可变二进制值，避免草稿修改原始基线。</summary>
    public static object? Clone(object? value) => value is byte[] bytes ? bytes.ToArray() : value;
    /// <summary>以协议要求的无损形式表示字段值。</summary>
    public static string Format(object? value) => value switch
    {
        null or DBNull => "NULL",
        byte[] bytes => Convert.ToHexString(bytes),
        Array array => JsonSerializer.Serialize(ArrayValues(array, new int[array.Rank], 0)),
        DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture),
        TimeSpan time => time.ToString("c", CultureInfo.InvariantCulture),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
    /// <summary>禁止有损编码和浮点特殊值，非法输入留在编辑器中。</summary>
    public static object? Parse(string text, PostgreSqlColumn column, bool isNull, string format = "text")
    {
        if (isNull) { if (!column.Nullable) throw Text.Exception("PostgreSqlNullNotAllowed"); return null; }
        try
        {
            if (column.ValueType == typeof(byte[]))
            {
                var bytes = format == "base64" ? Convert.FromBase64String(text) : Convert.FromHexString(text);
                if (bytes.Length > 10 * 1024 * 1024) throw Text.Exception("PostgreSqlFieldTooLarge");
                return bytes;
            }
            if (Encoding.UTF8.GetByteCount(text) > 10 * 1024 * 1024) throw Text.Exception("PostgreSqlFieldTooLarge");
            if (column.TypeName.StartsWith("JSON", StringComparison.OrdinalIgnoreCase)) { using var document = JsonDocument.Parse(text); return text; }
            if (column.ValueType == typeof(string)) return text;
            if (column.ValueType == typeof(Guid)) return Guid.Parse(text);
            if (column.ValueType == typeof(DateOnly)) return DateOnly.Parse(text, CultureInfo.InvariantCulture);
            if (column.ValueType == typeof(TimeOnly)) return TimeOnly.Parse(text, CultureInfo.InvariantCulture);
            if (column.ValueType == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture,
                column.TypeName.Contains("with time zone", StringComparison.OrdinalIgnoreCase) || column.TypeName == "timestamptz" ? DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal : DateTimeStyles.None);
            if (column.ValueType == typeof(TimeSpan)) return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
            if (column.ValueType == typeof(bool)) return text switch { "1" => true, "0" => false, _ => bool.Parse(text) };
            if (text.Contains(',')) throw new FormatException();
            object value = Convert.ChangeType(text, column.ValueType, CultureInfo.InvariantCulture);
            if (value is double d && !double.IsFinite(d) || value is float f && !float.IsFinite(f)) throw new FormatException();
            return value;
        }
        catch (JsonException error) { throw Text.Exception("PostgreSqlJsonInvalid", (error.LineNumber ?? 0) + 1); }
        catch (Exception error) when (error is FormatException or OverflowException or InvalidCastException or ArgumentException)
        { throw Text.Exception("PostgreSqlFieldInvalid"); }
    }
    /// <summary>估算原始数据大小，用于有界预览。</summary>
    public static long Bytes(object? value) => value switch { null => 0, byte[] bytes => bytes.LongLength, string text => Encoding.UTF8.GetByteCount(text), Array array => array.Cast<object?>().Sum(Bytes), _ => 32 };
    /// <summary>未实现无损输入的协议类型保持只读，不能靠 ToString 猜测序列化。</summary>
    public static bool SupportsInput(Type type) => type == typeof(string) || type == typeof(byte[]) || type == typeof(DateTime) || type == typeof(TimeSpan)
        || type == typeof(Guid) || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(decimal) || type.IsPrimitive && type != typeof(IntPtr) && type != typeof(UIntPtr);

    /// <summary>只读数组以 JSON 展示并保留多维形状，不显示 CLR 类型名称。</summary>
    private static object?[] ArrayValues(Array array, int[] indices, int dimension)
    {
        var values = new object?[array.GetLength(dimension)];
        for (int i = 0; i < values.Length; i++)
        {
            indices[dimension] = array.GetLowerBound(dimension) + i;
            values[i] = dimension + 1 < array.Rank ? ArrayValues(array, indices, dimension + 1) : array.GetValue(indices);
        }
        return values;
    }
}
