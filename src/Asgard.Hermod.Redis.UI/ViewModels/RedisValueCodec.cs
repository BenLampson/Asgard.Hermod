using System.Text.Json;

namespace Asgard.Hermod.Redis.UI.ViewModels;

/// <summary>格式只是展示和编辑方式，JSON 预览格式化不会隐式写回。</summary>
public static class RedisValueCodec
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    /// <summary>无损 UTF-8 验证，不使用替换字符。</summary>
    public static bool IsUtf8(byte[] bytes) { try { Utf8.GetString(bytes); return true; } catch (DecoderFallbackException) { return false; } }
    /// <summary>预览可格式化 JSON；编辑必须保留原始文本及空白。</summary>
    public static string Encode(byte[] bytes, string format, bool preview = false)
    {
        try
        {
            if (format == "base64") return Convert.ToBase64String(bytes);
            if (format == "hex") return Convert.ToHexString(bytes);
            string text = Utf8.GetString(bytes);
            if (format == "json" && preview) { using var document = JsonDocument.Parse(text); return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true }); }
            return text;
        }
        catch (Exception error) when (error is DecoderFallbackException or JsonException) { throw Text.Exception("RedisInvalidFormat"); }
    }
    /// <summary>提交前检查编码，错误不展示值或解析器原始信息。</summary>
    public static byte[] Decode(string value, string format)
    {
        try
        {
            if (format == "base64") return Convert.FromBase64String(value);
            if (format == "hex") return Convert.FromHexString(value);
            if (format == "json") { using var document = JsonDocument.Parse(value); }
            return Utf8.GetBytes(value);
        }
        catch (Exception error) when (error is FormatException or JsonException or EncoderFallbackException) { throw Text.Exception("RedisInvalidFormat"); }
    }
}
