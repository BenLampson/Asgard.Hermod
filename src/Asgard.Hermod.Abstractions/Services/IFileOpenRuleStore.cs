namespace Asgard.Hermod.Abstractions.Services;

/// <summary>本机文件打开规则的持久化边界，独立于云同步配置。</summary>
public interface IFileOpenRuleStore
{
    /// <summary>读取本机的全部规则，后缀唯一。</summary>
    IReadOnlyList<Models.FileOpenRule> Load();
    /// <summary>添加或编辑规则；编辑时传入原后缀，重命名与保存必须原子完成。</summary>
    void Save(Models.FileOpenRule rule, string? originalExtension = null);
    /// <summary>仅移除规则，不删除程序或文件。</summary>
    void Remove(string extension);
}
