namespace Asgard.Hermod.UI.ViewModels;

/// <summary>当前实例目录中的真实数据库；勾选仅用于新建或添加范围。</summary>
public sealed class DatabaseChoiceViewModel(DatabaseIdentity identity, bool included = false) : ObservableObject
{
    /// <summary>稳定实例与数据库身份，不根据显示标签判断归属。</summary>
    public DatabaseIdentity Identity { get; } = identity;
    /// <summary>用户数据库名称保持原文。</summary>
    public string Name => Identity.Name;
    /// <summary>已纳入项在添加弹窗只读展示，不能借取消勾选绕过移除确认。</summary>
    public bool IsIncluded { get; } = included;
    /// <summary>用户可以选择的新增候选。</summary>
    public bool CanSelect => !IsIncluded;
    /// <summary>尚未确认的选择，不改变已打开 Session。</summary>
    public bool IsSelected { get; set => SetProperty(ref field, value); }
}
