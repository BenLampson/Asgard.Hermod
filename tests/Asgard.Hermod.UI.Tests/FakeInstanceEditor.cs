namespace Asgard.Hermod.UI.Tests;

/// <summary>可控制表单取消和有效结果，不打开真实对话框。</summary>
internal sealed class FakeInstanceEditor : IInstanceEditor
{
    /// <summary>下次创建操作返回的配置；null 表示取消。</summary>
    internal InstanceSummary? NextResult { get; set; }
    /// <summary>可在创建结果返回前完成保存通知刷新，复现重复条目的实际事件顺序。</summary>
    internal Func<Task<InstanceSummary?>>? CreateHandler { get; set; }

    /// <inheritdoc />
    public Task<InstanceSummary?> CreateAsync() => CreateHandler?.Invoke() ?? Task.FromResult(NextResult);
}
