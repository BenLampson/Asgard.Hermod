using System.Text;
using System.Text.Json;

namespace Asgard.Hermod.PostgreSql;

/// <summary>按 JSON 指针记录对象的真实字符范围；重复子树和 Unicode 不通过文本猜测定位。</summary>
internal static class PostgreSqlPlanLocationIndex
{
    internal static IReadOnlyDictionary<string, (int Offset, int Length)> Read(string raw)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(raw);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 256 });
        var result = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);
        int previousByte = 0, previousCharacter = 0, count = 0;
        if (!reader.Read() || !Visit(ref reader, "", 0)) result.Clear();
        return result;

        int Character(long offset)
        {
            int next = checked((int)offset);
            previousCharacter += Encoding.UTF8.GetCharCount(bytes.AsSpan(previousByte, next - previousByte));
            previousByte = next; return previousCharacter;
        }
        bool Visit(ref Utf8JsonReader current, string path, int depth)
        {
            if (++count > 100000 || depth > 256) return false;
            if (current.TokenType == JsonTokenType.StartObject)
            {
                int start = Character(current.TokenStartIndex);
                while (current.Read() && current.TokenType != JsonTokenType.EndObject)
                {
                    if (current.TokenType != JsonTokenType.PropertyName) return false;
                    string name = current.GetString()!.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
                    if (!current.Read() || !Visit(ref current, path + "/" + name, depth + 1)) return false;
                }
                if (current.TokenType != JsonTokenType.EndObject) return false;
                result[path] = (start, Character(current.BytesConsumed) - start);
            }
            else if (current.TokenType == JsonTokenType.StartArray)
            {
                int index = 0;
                while (current.Read() && current.TokenType != JsonTokenType.EndArray)
                    if (!Visit(ref current, path + "/" + (index++).ToString(CultureInfo.InvariantCulture), depth + 1)) return false;
                if (current.TokenType != JsonTokenType.EndArray) return false;
            }
            return true;
        }
    }
}
