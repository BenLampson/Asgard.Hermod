using System.Runtime.CompilerServices;
using System.Text;

namespace Asgard.Hermod.Agent;

/// <summary>有界 SSE 解帧器；支持 UTF-8 跨包、多行 data、注释和三种换行。</summary>
internal static class AgentSseReader
{
    internal const int MaximumBytes = 1024 * 1024;
    internal static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);

    internal static async IAsyncEnumerable<string> ReadAsync(Stream stream, [EnumeratorCancellation] CancellationToken token, TimeProvider clock)
    {
        var decoder = new UTF8Encoding(false, true).GetDecoder();
        var bytes = new byte[4096]; var chars = new char[Encoding.UTF8.GetMaxCharCount(4096)];
        var line = new StringBuilder(); var data = new StringBuilder();
        int total = 0; bool afterCr = false, first = true;
        while (true)
        {
            int count;
            using (var idleLimit = new CancellationTokenSource(IdleTimeout, clock))
            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(token, idleLimit.Token))
                count = await stream.ReadAsync(bytes, idle.Token).ConfigureAwait(false);
            if (count == 0) break;
            total += count;
            if (total > MaximumBytes) throw Text.Exception("AgentInvalidResponse");
            int length = decoder.GetChars(bytes, 0, count, chars, 0);
            for (int i = 0; i < length; i++)
            {
                char ch = chars[i];
                if (first) { first = false; if (ch == '\uFEFF') continue; }
                if (ch == '\n' && afterCr) { afterCr = false; continue; }
                afterCr = ch == '\r';
                if (ch is not ('\r' or '\n')) { line.Append(ch); continue; }
                if (line.Length == 0)
                {
                    if (data.Length > 0) { string payload = data.ToString(0, data.Length - 1); data.Clear(); yield return payload; }
                }
                else
                {
                    string value = line.ToString(); line.Clear();
                    int colon = value.IndexOf(':');
                    string name = colon < 0 ? value : value[..colon];
                    if (name != "data") continue;
                    string fieldValue = colon < 0 ? "" : value[(colon + 1)..];
                    if (fieldValue.StartsWith(' ')) fieldValue = fieldValue[1..];
                    data.Append(fieldValue).Append('\n');
                }
            }
        }
        // EOF 不代替事件边界；调用方还必须收到完成原因和 [DONE]。
        decoder.GetChars([], 0, 0, chars, 0, true);
    }
}
