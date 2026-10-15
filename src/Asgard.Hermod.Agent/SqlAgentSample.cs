using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Asgard.Hermod.Agent;

/// <summary>自动查询只向模型返回有界样本，完整的已加载结果仍由模块显示。</summary>
public static class SqlAgentSample
{
    /// <summary>最多二十行、十六列和八千字节；二进制不发送，单个值只保留前二百五十六字符。</summary>
    public static string Create(IEnumerable<string> columns, IReadOnlyList<object?[]> rows, bool truncated)
    {
        string[] names = columns.Take(16).Select(value => value[..Math.Min(value.Length, 128)]).ToArray();
        var sample = rows.Take(20).Select(row => row.Take(16).Select(value =>
        {
            string text = value is byte[]? "[binary]" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL";
            return text[..Math.Min(text.Length, 256)];
        }).ToArray()).ToList();
        string Serialize() => JsonSerializer.Serialize(new { columns = names, rows = sample, sample = true, truncated = truncated || rows.Count > sample.Count });
        string output = Serialize();
        while (Encoding.UTF8.GetByteCount(output) > 8000 && sample.Count > 0) { sample.RemoveAt(sample.Count - 1); output = Serialize(); }
        return Encoding.UTF8.GetByteCount(output) <= 8000 ? output : JsonSerializer.Serialize(new { sample = true, truncated = true });
    }
}
