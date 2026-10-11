using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Asgard.Hermod.Infrastructure.Security;

namespace Asgard.Hermod.Services;

/// <summary>实例配置应用服务，负责第三方边界、参数保护和安全导航快照。</summary>
public sealed class LocalInstanceConfigurationService : IInstanceConfigurationService
{
    /// <summary>仅处理密文和 SQLite 事务的仓储。</summary>
    private readonly ILocalConfigurationRepository _repository;
    /// <summary>扩展服务商的唯一注册入口。</summary>
    private readonly IInstanceProviderRegistry _providers;
    /// <summary>当前设备用户的凭据保护实现。</summary>
    private readonly ISecretProtector _protector;

    /// <summary>界面只依赖服务合同，不能获得数据库或凭据保护器。</summary>
    public LocalInstanceConfigurationService(ILocalConfigurationRepository repository,
        IInstanceProviderRegistry providers, ISecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(protector);
        _repository = repository;
        _providers = providers;
        _protector = protector;
    }

    /// <inheritdoc />
    public IReadOnlyList<InstanceSummary> GetInstances()
    {
        LocalConfigurationSnapshot snapshot = _repository.GetSnapshot();
        return snapshot.Instances.Select(instance => ToSummary(instance, snapshot.Tags)).ToArray();
    }

    /// <inheritdoc />
    public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft,
        CancellationToken cancellationToken = default) => SaveAsync(null, draft, cancellationToken);

    /// <inheritdoc />
    public Task<InstanceSummary> UpdateAsync(string instanceId, InstanceConfigurationDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return SaveAsync(instanceId, draft, cancellationToken);
    }

    /// <summary>新增和编辑共用字段校验与密文保护，并在提交前完成全部导航读取。</summary>
    private Task<InstanceSummary> SaveAsync(string? instanceId, InstanceConfigurationDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();
        if (draft.SaveLocation != InstanceSaveLocation.Local)
            throw new InvalidOperationException("账户存储暂未接入，请选择保存在本地设备。");
        string name = (draft.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
            throw new InvalidOperationException("实例名称须为 1–128 个字符，且不能包含控制字符。");
        ArgumentNullException.ThrowIfNull(draft.Connection);
        // 类型和服务商必须共同匹配，未知插件不能将数据伪装为可用实例保存。
        IInstanceProvider provider = _providers.GetProvider(draft.Connection.TypeId, draft.Connection.ProviderId);
        ArgumentNullException.ThrowIfNull(draft.Connection.Parameters);
        if (draft.Connection.Parameters.Count > 100 ||
            draft.Connection.Parameters.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 128 ||
                pair.Key.Any(char.IsControl) || pair.Value is null || pair.Value.Length > 65536))
            throw new InvalidOperationException("连接参数无效，请检查服务商要求的配置字段。");
        ArgumentNullException.ThrowIfNull(draft.TagIds);
        var parameters = new Dictionary<string, string>(draft.Connection.Parameters, StringComparer.Ordinal);
        foreach (InstanceConfigurationField field in provider.Descriptor.Fields)
        {
            string error = InstanceFieldValidation.GetError(field, parameters.GetValueOrDefault(field.Key));
            if (error.Length != 0) throw new InvalidOperationException(error);
        }
        string[] requestedTagIds = draft.TagIds.Distinct(StringComparer.Ordinal).ToArray();
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(parameters);
            try
            {
                byte[] protectedParameters = _protector.Protect(plaintext);
                LocalConfigurationSnapshot snapshot = _repository.GetSnapshot(cancellationToken);
                StoredInstanceConfiguration? existing = instanceId is null ? null :
                    snapshot.Instances.FirstOrDefault(value => value.Id == instanceId)
                    ?? throw new InvalidOperationException("实例已不存在，请刷新后重新配置。");
                if (existing is not null && (existing.TypeId != provider.Descriptor.Type.Id || existing.ProviderId != provider.Descriptor.Id))
                    throw new InvalidOperationException("编辑实例不能更改实例类型或服务商，请新增对应类型的实例。");
                var instance = new StoredInstanceConfiguration(existing?.Id ?? Guid.NewGuid().ToString("N"), name,
                    provider.Descriptor.Type.Id, provider.Descriptor.Id, protectedParameters,
                    existing?.CreatedAt ?? DateTime.UtcNow, existing?.TagIds ?? requestedTagIds);
                // 提交前准备所有导航字段；提交后再查询可能把已保存操作误报成失败。
                InstanceSummary summary = ToSummary(instance, snapshot.Tags);
                if (existing is null) _repository.CreateInstance(instance, cancellationToken);
                else _repository.UpdateInstance(instance, cancellationToken);
                // 后续标签改名通过 GetInstances 刷新路径，保存结果不依赖额外数据库读取。
                return summary;
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return Task.Run(() =>
        {
            StoredInstanceConfiguration? instance = _repository.GetInstance(instanceId, cancellationToken);
            if (instance is null) return null;
            cancellationToken.ThrowIfCancellationRequested();
            // 第三方插件缺失时明确失败，不把其凭据交给其他服务商。
            _providers.GetProvider(instance.TypeId, instance.ProviderId);
            byte[] plaintext = _protector.Unprotect(instance.ProtectedParameters);
            try
            {
                var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext)
                    ?? throw new InvalidOperationException("连接配置损坏，请重新配置实例。");
                cancellationToken.ThrowIfCancellationRequested();
                return new InstanceConnectionOptions
                {
                    TypeId = instance.TypeId,
                    ProviderId = instance.ProviderId,
                    Parameters = new ReadOnlyDictionary<string, string>(parameters),
                };
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("连接配置损坏，请重新配置实例。");
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }, cancellationToken);
    }

    /// <summary>导航保留名称和类型身份，只展示安全服务商元数据，不解析连接参数或密文。</summary>
    private InstanceSummary ToSummary(StoredInstanceConfiguration instance, IReadOnlyList<ManagedTag> tags)
    {
        InstanceProviderDescriptor? descriptor = _providers.Types.Any(type => type.Id == instance.TypeId)
            ? _providers.GetProviders(instance.TypeId).FirstOrDefault(value => value.Id == instance.ProviderId)
            : null;
        HashSet<string> tagIds = instance.TagIds.ToHashSet(StringComparer.Ordinal);
        return new InstanceSummary(instance.Id, instance.Name, instance.Name,
            descriptor?.Icon ?? "database", tags.Where(tag => tagIds.Contains(tag.Id)).Select(tag => tag.Path).ToArray(),
            Endpoint: descriptor?.Name ?? "未安装的实例连接器", TypeId: instance.TypeId, ProviderId: instance.ProviderId);
    }
}
