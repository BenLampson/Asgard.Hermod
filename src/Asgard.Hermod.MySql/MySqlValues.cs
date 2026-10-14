using System.Text;
using System.Text.Json;

namespace Asgard.Hermod.MySql;

/// <summary>类型转换使用 invariant；SQL NULL 与空字符串不同。</summary>
public static class MySqlValues
{
    public static bool Equal(object? left, object? right) => left is byte[] a && right is byte[] b ? a.AsSpan().SequenceEqual(b) : Equals(left, right);
    public static object? Clone(object? value) => value is byte[] bytes ? bytes.ToArray() : value;
    public static string Format(object? value) => value switch
    {
        null or DBNull => "NULL",
        byte[] bytes => Convert.ToHexString(bytes),
        DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture),
        TimeSpan time => time.ToString("c", CultureInfo.InvariantCulture),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
    /// <summary>禁止有损编码和浮点特殊值，非法输入留在编辑器中。</summary>
    public static object? Parse(string text, MySqlColumn column, bool isNull, string format = "text")
    {
        if (isNull) { if (!column.Nullable) throw Text.Exception("MySqlNullNotAllowed"); return null; }
        try
        {
            if (column.ValueType == typeof(byte[])) return format == "base64" ? Convert.FromBase64String(text) : Convert.FromHexString(text);
            if (column.TypeName.StartsWith("JSON", StringComparison.OrdinalIgnoreCase)) { using var document = JsonDocument.Parse(text); return text; }
            if (column.ValueType == typeof(string)) return text;
            if (column.ValueType == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.None);
            if (column.ValueType == typeof(TimeSpan)) return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
            if (column.ValueType == typeof(bool)) return text switch { "1" => true, "0" => false, _ => bool.Parse(text) };
            object value = Convert.ChangeType(text, column.ValueType, CultureInfo.InvariantCulture);
            if (value is double d && !double.IsFinite(d) || value is float f && !float.IsFinite(f)) throw new FormatException();
            return value;
        }
        catch (JsonException error) { throw Text.Exception("MySqlJsonInvalid", (error.LineNumber ?? 0) + 1); }
        catch (Exception error) when (error is FormatException or OverflowException or InvalidCastException or ArgumentException)
        { throw Text.Exception("MySqlFieldInvalid"); }
    }
    public static long Bytes(object? value) => value switch { null => 0, byte[] bytes => bytes.LongLength, string text => Encoding.UTF8.GetByteCount(text), _ => 32 };
}
