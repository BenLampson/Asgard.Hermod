namespace Asgard.Hermod.UI.ViewModels;

public sealed partial class NewInstanceViewModel
{
    /// <summary>SSH 与数据库在确认页选择独立品牌，连接方式和拓扑仍由首步决定。</summary>
    public bool HasHostingProviderSelection => SelectedType is not null && InstanceHostingProviders.Supports(SelectedType.Id);

    /// <summary>修改展示品牌不作废已经通过的连接测试。</summary>
    public string HostingProviderId
    {
        get;
        set { if (IsInformationEditable && InstanceHostingProviders.IsValid(value)) SetProperty(ref field, value); }
    } = "";
}
