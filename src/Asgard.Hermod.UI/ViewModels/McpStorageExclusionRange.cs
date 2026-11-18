namespace Asgard.Hermod.UI.ViewModels;

/// <summary>在对象存储编辑器中明确选择禁止范围，留空字段不会扩大影响。</summary>
public enum McpStorageExclusionRange
{
    /// <summary>禁止匹配的对象键或目录。</summary>
    Prefix,
    /// <summary>禁止一个 Bucket。</summary>
    Bucket,
    /// <summary>禁止整个实例。</summary>
    Instance,
}
