namespace Asgard.Hermod.Abstractions.Services;

/// <summary>保存当前设备上的语言偏好，空值表示跟随 Windows。</summary>
public interface ILanguagePreferenceStore
{
    /// <summary>读取持久化的语言标识，未设置时返回空值。</summary>
    string? Load();
    /// <summary>原子保存手动选择；空值恢复系统语言。</summary>
    void Save(string? languageId);
}
