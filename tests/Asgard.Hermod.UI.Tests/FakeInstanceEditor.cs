namespace Asgard.Hermod.UI.Tests;

/// <summary>可控制表单取消和有效结果，不打开真实对话框。</summary>
internal sealed class FakeInstanceEditor : IInstanceEditor
{
    /// <summary>下次创建操作返回的配置；null 表示取消。</summary>
    internal InstanceSummary? NextResult { get; set; }

    /// <inheritdoc />
    public Task<InstanceSummary?> CreateAsync() => Task.FromResult(NextResult);
}
