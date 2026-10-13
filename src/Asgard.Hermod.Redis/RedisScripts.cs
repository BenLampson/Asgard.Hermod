namespace Asgard.Hermod.Redis;

/// <summary>协议脚本是嵌入资源，不在 UI 线程读取文件。</summary>
internal static class RedisScripts
{
    internal static readonly string Read = Load("read.lua");
    internal static readonly string CompareWrite = Load("compare-write.lua");
    internal const string Expiry = "if redis.call('EXISTS',KEYS[1]) == 0 then return 0 end if ARGV[1] == '-1' then redis.call('PERSIST',KEYS[1]) else redis.call('PEXPIRE',KEYS[1],ARGV[1]) end return 1";
    internal const string Append = "local t=redis.call('TYPE',KEYS[1]).ok if t~='none' and t~='stream' then return 0 end redis.call('XADD',KEYS[1],'*',unpack(ARGV)) return 1";
    private static string Load(string name)
    {
        using var stream = typeof(RedisScripts).Assembly.GetManifestResourceStream("Asgard.Hermod.Redis.Scripts." + name)!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
}
