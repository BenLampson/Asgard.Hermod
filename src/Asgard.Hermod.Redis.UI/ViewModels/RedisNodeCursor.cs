namespace Asgard.Hermod.Redis.UI.ViewModels;

/// <summary>每个前缀、每个主节点独立保留 SCAN 进度。</summary>
internal sealed class RedisNodeCursor
{
    internal string Cursor { get; set; } = "0";
    internal bool Complete { get; set; }
    internal bool Failed { get; set; }
}
