using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Asgard.Hermod.Abstractions.Security;

namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>实例配置应用服务，负责第三方边界、参数保护和安全导航快照。</summary>
public sealed partial class LocalInstanceConfigurationService : IInstanceConfigurationService
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
    public Task<IReadOnlyList<string>> GetTagIdsAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return Task.Run<IReadOnlyList<string>>(() => _repository.GetInstance(instanceId, cancellationToken)?.TagIds.ToArray()
            ?? throw Text.Exception("TheInstanceNoLongerExistsRefreshAndConfigureItAgain"), cancellationToken);
    }

    /// <inheritdoc />
    public Task<InstanceSummary> UpdateAsync(string instanceId, InstanceConfigurationDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return SaveAsync(instanceId, draft, cancellationToken);
    }

    /// <summary>新增和编辑共用字段校验与密文保护，并在提交前完成全部导航读取。</summary>
    private async Task<InstanceSummary> SaveAsync(string? instanceId, InstanceConfigurationDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();
        if (draft.SaveLocation != InstanceSaveLocation.Local)
            throw Text.Exception("AccountStorageIsNotAvailableYetSaveOnThisDevice");
        string name = (draft.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
            throw Text.Exception("InstanceNamesMustContain1128CharactersAndNoControlCharacters");
        ArgumentNullException.ThrowIfNull(draft.Connection);
        // 类型和服务商必须共同匹配，未知插件不能将数据伪装为可用实例保存。
        IInstanceProvider provider = _providers.GetProvider(draft.Connection.TypeId, draft.Connection.ProviderId);
        ArgumentNullException.ThrowIfNull(draft.Connection.Parameters);
        if (draft.Connection.Parameters.Count > 100 ||
            draft.Connection.Parameters.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 128 ||
                pair.Key.Any(char.IsControl) || pair.Value is null || pair.Value.Length > 65536))
            throw Text.Exception("InvalidConnectionParametersCheckTheFieldsRequiredByTheProvider");
        ArgumentNullException.ThrowIfNull(draft.TagIds);
        var prepared = await provider.PrepareConnectionAsync(draft.Connection, cancellationToken).ConfigureAwait(false);
        if (prepared.TypeId != draft.Connection.TypeId || prepared.ProviderId != draft.Connection.ProviderId
            || prepared.Parameters.Count > 100 || prepared.Parameters.Any(pair => string.IsNullOrWhiteSpace(pair.Key)
                || pair.Key.Length > 128 || pair.Key.Any(char.IsControl) || pair.Value is null || pair.Value.Length > 65536))
            throw Text.Exception("InvalidConnectionParametersCheckTheFieldsRequiredByTheProvider");
        var parameters = new Dictionary<string, string>(prepared.Parameters, StringComparer.Ordinal);
        foreach (InstanceConfigurationField field in provider.Descriptor.Fields)
        {
            if (!InstanceFieldValidation.IsApplicable(field, parameters))
            {
                parameters.Remove(field.Key);
                continue;
            }
            string error = InstanceFieldValidation.GetError(field, parameters.GetValueOrDefault(field.Key));
            if (error.Length != 0) throw Text.Exception("InvalidConnectionParametersCheckTheFieldsRequiredByTheProvider");
        }
        string[] requestedTagIds = draft.TagIds.Distinct(StringComparer.Ordinal).ToArray();
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(parameters);
            try
            {
                byte[] protectedParameters = _protector.Protect(plaintext);
                LocalConfigurationSnapshot snapshot = _repository.GetSnapshot(cancellationToken);
                StoredInstanceConfiguration? existing = instanceId is null ? null :
                    snapshot.Instances.FirstOrDefault(value => value.Id == instanceId)
                    ?? throw Text.Exception("TheInstanceNoLongerExistsRefreshAndConfigureItAgain");
                if (existing is not null && (existing.TypeId != provider.Descriptor.Type.Id
                    || existing.ProviderId != provider.Descriptor.Id && !provider.Descriptor.Type.AllowProviderChangeOnEdit))
                    throw Text.Exception("EditingCannotChangeTheInstanceTypeOrProviderCreateAnInstanceOfTheRequiredTypeInstead");
                var instance = new StoredInstanceConfiguration(existing?.Id ?? Guid.NewGuid().ToString("N"), name,
                    provider.Descriptor.Type.Id, provider.Descriptor.Id, protectedParameters,
                    existing?.CreatedAt ?? DateTime.UtcNow,
                    existing is not null && !draft.ReplaceTags ? existing.TagIds : requestedTagIds);
                // 提交前准备所有导航字段；提交后再查询可能把已保存操作误报成失败。
                InstanceSummary summary = ToSummary(instance, snapshot.Tags);
                if (existing is null) _repository.CreateInstance(instance, cancellationToken);
                else _repository.UpdateInstance(instance, draft.ReplaceTags, cancellationToken);
                // 后续标签改名通过 GetInstances 刷新路径，保存结果不依赖额外数据库读取。
                return summary;
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }, cancellationToken).ConfigureAwait(false);
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
                    ?? throw Text.Exception("TheConnectionConfigurationIsCorruptedConfigureTheInstanceAgain");
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
                throw Text.Exception("TheConnectionConfigurationIsCorruptedConfigureTheInstanceAgain");
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
        return CreateSummary(instance, tags, descriptor);
    }

    /// <summary>提交后只使用已经准备好的描述，不调用插件或数据库。</summary>
    private static InstanceSummary CreateSummary(StoredInstanceConfiguration instance, IReadOnlyList<ManagedTag> tags, InstanceProviderDescriptor? descriptor)
    {
        HashSet<string> tagIds = instance.TagIds.ToHashSet(StringComparer.Ordinal);
        return new InstanceSummary(instance.Id, instance.Name, instance.Name,
            descriptor?.Icon ?? "database", tags.Where(tag => tagIds.Contains(tag.Id)).Select(tag => tag.Path).ToArray(),
            Endpoint: descriptor?.Name ?? Text.Get("InstanceConnectorNotInstalled"), TypeId: instance.TypeId, ProviderId: instance.ProviderId)
        { TagIds = instance.TagIds.ToArray() };
    }
}
