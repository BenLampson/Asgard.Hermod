namespace Asgard.Hermod.UI.ViewModels;

/// <summary>每项必须明确选择，参数预览由同步服务提前遮蔽。</summary>
public sealed class ConfigurationConflictViewModel(ConfigurationConflict conflict) : ObservableObject
{
    /// <summary>版本范围内的稳定冲突标识。</summary>
    public string Key => conflict.Key;
    /// <summary>用户创建的实例或标签名称。</summary>
    public string EntityName => conflict.EntityName;
    /// <summary>参数键保持协议原文，完整句式由资源提供。</summary>
    public string FieldLabel => conflict.Key.Contains(":parameter:", StringComparison.Ordinal)
        ? Text.Format("SyncConflictParameter", conflict.Key[(conflict.Key.IndexOf(":parameter:", StringComparison.Ordinal) + 11)..])
        : Text.Get(conflict.Field);
    /// <summary>安全本地预览。</summary>
    public string LocalValue => Text.Format("SyncConflictLocalValue", conflict.LocalValue);
    /// <summary>安全云端预览。</summary>
    public string CloudValue => Text.Format("SyncConflictCloudValue", conflict.CloudValue);
    /// <summary>固定选项索引是协议行为，绝不比较翻译标签。</summary>
    public int SelectedIndex { get; set => SetProperty(ref field, value); } = -1;
}
