namespace Asgard.Hermod.MySql;

/// <summary>协议和保存配置使用稳定标识，不依赖翻译。</summary>
public static class MySqlIds
{
    /// <summary>持久化使用的 MySQL 类型标识。</summary>
    public const string Type = "mysql";
    /// <summary>直接 TCP 连接方法标识。</summary>
    public const string Direct = "mysql.direct";
    /// <summary>SSH 转发连接方法标识。</summary>
    public const string Ssh = "mysql.ssh";
}
