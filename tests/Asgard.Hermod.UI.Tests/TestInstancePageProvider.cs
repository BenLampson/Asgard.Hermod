namespace Asgard.Hermod.UI.Tests;

/// <summary>模拟另一种实例页面提供者，验证工厂只依赖顶层类型身份。</summary>
internal sealed class TestInstancePageProvider(string typeId) : IInstancePageProvider
{
    /// <inheritdoc />
    public string TypeId => typeId;
    /// <summary>统计页面创建次数。</summary>
    internal int CreatedPages { get; private set; }
    /// <inheritdoc />
    public WorkspacePageViewModel CreatePage(InstanceItemViewModel instance)
    {
        CreatedPages++;
        return new ServiceDetailViewModel(instance, null);
    }
}
