namespace Asgard.Hermod.Abstractions.Models;

/// <summary>账号工作区的模型集合；空集合是明确清除，区别于旧客户端未提供该字段。</summary>
public sealed record AgentModelSet(IReadOnlyList<AgentModelProfile> Profiles, string DefaultProfileId = "")
{
    /// <summary>尚未配置模型的不可变集合。</summary>
    public static AgentModelSet Empty { get; } = new(Array.Empty<AgentModelProfile>());
    /// <summary>隔离调用方可变集合，配置记录本身不可变。</summary>
    public AgentModelSet Freeze() => this with { Profiles = Array.AsReadOnly(Profiles.ToArray()) };
    /// <summary>集合调试信息不展开凭据。</summary>
    public override string ToString() => nameof(AgentModelSet);
}
