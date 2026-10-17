namespace Asgard.Hermod.Abstractions.Services;

/// <summary>保存当前设备保护的会话及按账号隔离的同步版本；不得记录载荷。</summary>
public interface IAccountSessionStore
{
    /// <summary>读取受当前设备用户保护的会话。</summary>
    string? LoadSession();
    /// <summary>原子替换会话，空值表示清除。</summary>
    void SaveSession(string? session);
    /// <summary>读取与当前用户、服务器共同绑定的已知版本。</summary>
    string LoadRevision(string owner);
    /// <summary>只有上传成功或本地恢复成功后保存版本。</summary>
    void SaveRevision(string owner, string revision);
}
