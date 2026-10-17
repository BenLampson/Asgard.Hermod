namespace Asgard.Hermod.UI.Tests;

/// <summary>可控语言存储替身，只用于设置流程测试。</summary>
internal sealed class LanguagePreferenceTestStore : ILanguagePreferenceStore
{
    /// <summary>持久化成功后保留的语言标识。</summary>
    public string? Saved { get; private set; }
    /// <summary>模拟本地保存失败。</summary>
    public bool FailSave { get; set; }
    /// <inheritdoc />
    public string? Load() => Saved;
    /// <inheritdoc />
    public void Save(string? languageId)
    {
        if (FailSave) throw new IOException("private-database-path");
        Saved = languageId;
    }
}
