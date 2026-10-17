namespace Asgard.Hermod.UI.Tests;

/// <summary>模拟现有连接编辑成功或取消，不打开向导。</summary>
internal sealed class ObjectStorageTestInstanceEditor : IInstanceEditor
{
    /// <summary>下次编辑返回的安全摘要，空值表示取消。</summary>
    internal InstanceSummary? Result { get; set; }
    /// <summary>记录被编辑的实例标识。</summary>
    internal string? EditedId { get; private set; }
    /// <inheritdoc />
    public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    /// <inheritdoc />
    public Task<InstanceSummary?> EditAsync(string instanceId) { EditedId = instanceId; return Task.FromResult(Result); }
}
