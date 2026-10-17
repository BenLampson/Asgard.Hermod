using System.Text;

namespace Asgard.Hermod.PostgreSql;

internal sealed partial class PostgreSqlSession
{
    /// <summary>大文本和二进制逐块读取，先限制预算再扩展内存，避免 SDK 一次分配整个字段。</summary>
    private static async Task<(object? Value, long Bytes, bool TooLarge)> ReadValueAsync(NpgsqlDataReader reader, int ordinal, long remaining, CancellationToken token)
    {
        if (await reader.IsDBNullAsync(ordinal, token).ConfigureAwait(false)) return (null, 0, false);
        if (reader.GetFieldType(ordinal) == typeof(string))
        {
            using var textReader = reader.GetTextReader(ordinal);
            var text = new StringBuilder(); var buffer = new char[4096]; long bytes = 0;
            while (true)
            {
                int read = await textReader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                if (read == 0) return (text.ToString(), bytes, false);
                bytes += Encoding.UTF8.GetByteCount(buffer.AsSpan(0, read));
                if (bytes > remaining) return (null, bytes, true);
                text.Append(buffer, 0, read);
            }
        }
        if (reader.GetFieldType(ordinal) == typeof(byte[]))
        {
            using var stream = reader.GetStream(ordinal); using var data = new MemoryStream(); var buffer = new byte[4096];
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                if (read == 0) return (data.ToArray(), data.Length, false);
                if (data.Length + read > remaining) return (null, data.Length + read, true);
                data.Write(buffer, 0, read);
            }
        }
        object value = reader.GetValue(ordinal); long size = PostgreSqlValues.Bytes(value);
        return (value, size, size > remaining);
    }
}
