namespace Asgard.Hermod.Abstractions.Services;

/// <summary>按当前本地用户保存首次托盘隐藏提示标记，不参与云端配置同步。</summary>
public interface ITrayPreferenceStore
{
    /// <summary>是否已经提示过主窗体隐藏后的托盘入口。</summary>
    bool HasShownHideHint();
    /// <summary>在首次隐藏时立即持久化，系统禁止气泡也不重复提示。</summary>
    void MarkHideHintShown();
}
