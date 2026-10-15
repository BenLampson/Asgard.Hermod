using System.Text;

namespace Asgard.Hermod.Agent;

/// <summary>只提取完整 Markdown SQL 围栏；不解释、不验证、更不执行 SQL。</summary>
public static class AgentSqlBlocks
{
    /// <summary>按出现顺序返回 SQL 方言围栏，忽略其他语言及未闭合内容。</summary>
    public static IReadOnlyList<string> Extract(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var blocks = new List<string>();
        var buffer = new StringBuilder();
        char marker = '\0'; int fenceLength = 0; bool sql = false;
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.TrimStart();
            if (marker == '\0')
            {
                if (trimmed.Length < 3 || trimmed[0] is not ('`' or '~')) continue;
                int count = trimmed.TakeWhile(character => character == trimmed[0]).Count();
                if (count < 3) continue;
                marker = trimmed[0]; fenceLength = count;
                string language = trimmed[count..].Trim();
                sql = language.Equals("sql", StringComparison.OrdinalIgnoreCase) || language.Equals("mysql", StringComparison.OrdinalIgnoreCase) ||
                    language.Equals("postgresql", StringComparison.OrdinalIgnoreCase) || language.Equals("postgres", StringComparison.OrdinalIgnoreCase);
                buffer.Clear();
            }
            else if (trimmed.Length >= fenceLength && trimmed.TakeWhile(character => character == marker).Count() >= fenceLength &&
                trimmed.Trim(marker).Trim().Length == 0)
            {
                string statement = buffer.ToString().Trim();
                if (sql && statement.Length > 0) blocks.Add(statement);
                marker = '\0'; sql = false;
            }
            else if (sql) buffer.AppendLine(line);
        }
        return blocks.AsReadOnly();
    }
}
