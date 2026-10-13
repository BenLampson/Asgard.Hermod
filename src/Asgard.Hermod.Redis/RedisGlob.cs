namespace Asgard.Hermod.Redis;

/// <summary>对原始字节匹配 Redis glob，前缀扫描和用户 MATCH 分别应用，避免把字面前缀当表达式。</summary>
public static class RedisGlob
{
    /// <summary>支持 *、?、转义及 [] 范围；使用备忘录限制重复回溯。</summary>
    public static bool IsMatch(byte[] value, byte[] pattern)
    {
        var memo = new Dictionary<(int, int), bool>();
        return Match(0, 0);
        bool Match(int i, int p)
        {
            if (memo.TryGetValue((i, p), out bool cached)) return cached;
            int startI = i, startP = p;
            bool result;
            if (p == pattern.Length) result = i == value.Length;
            else if (pattern[p] == '*')
            {
                while (p + 1 < pattern.Length && pattern[p + 1] == '*') p++;
                result = Match(i, p + 1) || i < value.Length && Match(i + 1, p);
            }
            else if (i == value.Length) result = false;
            else if (pattern[p] == '?') result = Match(i + 1, p + 1);
            else if (pattern[p] == '\\') result = p + 1 < pattern.Length
                ? value[i] == pattern[p + 1] && Match(i + 1, p + 2) : value[i] == '\\' && Match(i + 1, p + 1);
            else if (pattern[p] == '[')
            {
                int end = p + 1; bool negate = end < pattern.Length && pattern[end] == '^'; if (negate) end++;
                bool selected = false;
                while (end < pattern.Length && pattern[end] != ']')
                {
                    byte low = pattern[end++];
                    if (low == '\\' && end < pattern.Length) low = pattern[end++];
                    if (end + 1 < pattern.Length && pattern[end] == '-' && pattern[end + 1] != ']')
                    { end++; byte high = pattern[end++]; selected |= value[i] >= Math.Min(low, high) && value[i] <= Math.Max(low, high); }
                    else selected |= value[i] == low;
                }
                result = end < pattern.Length ? selected != negate && Match(i + 1, end + 1) : value[i] == '[' && Match(i + 1, p + 1);
            }
            else result = value[i] == pattern[p] && Match(i + 1, p + 1);
            memo[(startI, startP)] = result; return result;
        }
    }
}
