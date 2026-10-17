namespace Asgard.Hermod.Redis.UI.ViewModels;

/// <summary>稳定的数据库选项；只更新名称，刷新数量不重置 ComboBox 的选择或触发查询。</summary>
public sealed class RedisDatabaseViewModel(int database) : ObservableObject
{
    private long? _count;
    /// <summary>数据库的真实编号。</summary>
    public int Database { get; } = database;
    /// <summary>供下拉选择绑定的稳定标识。</summary>
    public string Id => Database.ToString(CultureInfo.InvariantCulture);
    /// <summary>仅展示已读取的实际数量；无权限或断线使用明确的不可用标记。</summary>
    public string Name => Text.Format("RedisDatabaseWithCount", Database, _count?.ToString(CultureInfo.InvariantCulture) ?? Text.Get("RedisDatabaseCountUnknown"));
    /// <summary>仅更新显示值，保持选项对象身份不变。</summary>
    public void SetCount(long? count) { if (_count == count) return; _count = count; OnPropertyChanged(nameof(Name)); }
}
