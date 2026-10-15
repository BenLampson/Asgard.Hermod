namespace Asgard.Hermod.Abstractions.Services;

/// <summary>本机 AI 设置存储，不参与账号同步，也不接受 API 密钥。</summary>
public interface IAgentPreferenceStore
{
    /// <summary>读取模型服务地址和模型名。</summary>
    Models.AgentPreferences Load();
    /// <summary>原子保存非敏感配置。</summary>
    void Save(Models.AgentPreferences preferences);
}
