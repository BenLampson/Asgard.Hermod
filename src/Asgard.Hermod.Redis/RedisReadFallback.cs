namespace Asgard.Hermod.Redis;

/// <summary>没有 EVAL_RO 权限时使用只读命令展示已知键，结果不参与任何写入基线。</summary>
internal static class RedisReadFallback
{
    internal static async Task<RedisSnapshot> ReadAsync(IDatabase database, RedisKeyId key, string cursor, long offset, CancellationToken token)
    {
        async Task<RedisResult> Call(string command, params object[] args) => await database.ExecuteAsync(command, args, CommandFlags.DemandMaster).WaitAsync(token).ConfigureAwait(false);
        var raw = key.ToBytes(); string type = (string)(await Call("TYPE", raw).ConfigureAwait(false))!;
        if (type == "none") return RedisSnapshot.Missing;
        var rows = new List<RedisEntry>(); bool more = false; string next = "0";
        string lengthCommand = type switch { "string" => "STRLEN", "hash" => "HLEN", "list" => "LLEN", "set" => "SCARD", "zset" => "ZCARD", "stream" => "XLEN", _ => "" };
        if (lengthCommand.Length == 0) return new(type, 0, -1, [], false);
        long length = (long)await Call(lengthCommand, raw).ConfigureAwait(false);
        if (type == "string") rows.Add(new([], (byte[])(await Call("GETRANGE", raw, 0, RedisSnapshot.MaximumBytes - 1).ConfigureAwait(false))!));
        else if (type == "list")
        {
            var values = (RedisResult[])(await Call("LRANGE", raw, offset, offset + 99).ConfigureAwait(false))!;
            for (int i = 0; i < values.Length; i++) rows.Add(new(Encoding.ASCII.GetBytes((offset + i).ToString(CultureInfo.InvariantCulture)), (byte[])values[i]!));
            offset += values.Length; more = offset < length;
        }
        else if (type == "stream")
        {
            var records = (RedisResult[])(await Call("XRANGE", raw, cursor == "0" ? "-" : "(" + cursor, "+", "COUNT", 100).ConfigureAwait(false))!;
            foreach (var record in records)
            {
                var tuple = (RedisResult[])record!; next = (string)tuple[0]!;
                var fields = (RedisResult[])tuple[1]!;
                for (int i = 0; i < fields.Length; i += 2) rows.Add(new((byte[])fields[i]!, (byte[])fields[i + 1]!, RecordId: next));
            }
            offset += records.Length; more = offset < length;
        }
        else
        {
            string command = type == "hash" ? "HSCAN" : type == "set" ? "SSCAN" : "ZSCAN";
            var page = (RedisResult[])(await Call(command, raw, cursor, "COUNT", 100).ConfigureAwait(false))!;
            next = (string)page[0]!; more = next != "0";
            var values = (RedisResult[])page[1]!;
            for (int i = 0; i < values.Length; i += type == "set" ? 1 : 2)
                rows.Add(new((byte[])values[i]!, type == "hash" ? (byte[])values[i + 1]! : [], type == "zset" ? (string)values[i + 1]! : ""));
        }
        long budget = 0;
        var bounded = rows.Where(row => { budget += row.Name.Length + row.Value.Length; return budget <= RedisSnapshot.MaximumBytes; }).ToArray();
        long ttl = (long)await Call("PTTL", raw).ConfigureAwait(false);
        return new(type, length, ttl, bounded, false, next, offset, more, budget > RedisSnapshot.MaximumBytes || type == "string" && length > RedisSnapshot.MaximumBytes);
    }
}
