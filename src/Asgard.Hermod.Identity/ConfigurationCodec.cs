namespace Asgard.Hermod.Identity;

/// <summary>连接器声明的非敏感字段可读，敏感与未知字段保守加密。</summary>
internal sealed class ConfigurationCodec(IInstanceProviderRegistry registry)
{
    /// <summary>下载先验证校验包，再逐条解密；旧整包在客户端迁移。</summary>
    public WorkspaceSnapshot Decode(CloudConfiguration cloud, string password, string owner)
    {
        if (cloud.LegacyWorkspace is not null) return WorkspaceEncryption.Decrypt(cloud.LegacyWorkspace, password, owner);
        if (cloud.Protection is null) return new(1, [], []);
        using var secrets = new ConfigurationSecrets(password, owner, cloud.Protection);
        return new(1, cloud.Instances.Select(item =>
        {
            var parameters = new Dictionary<string, string>(item.Parameters, StringComparer.Ordinal);
            foreach (var pair in secrets.Decrypt(item))
                if (!parameters.TryAdd(pair.Key, pair.Value)) throw Text.Exception("SyncSnapshotInvalid");
            parameters.Remove(InstanceHostingProviders.MetadataKey, out string? hosting);
            return new SyncedInstance(item.Id, item.Name, item.TypeId, item.ProviderId, parameters, item.CreatedAt, item.TagIds)
            { HostingProviderId = hosting ?? "" };
        }).ToArray(), cloud.Tags.Select(item => item.Tag).ToArray());
    }

    /// <summary>未变化条目复用原密文，避免每次检查都制造新版本。</summary>
    public CloudConfiguration Encode(WorkspaceSnapshot snapshot, WorkspaceSnapshot original, CloudConfiguration cloud, string password, string owner)
    {
        var protection = cloud.Protection ?? WorkspaceEncryption.Encrypt(new(1, [], []), password, owner + ":configuration");
        using var secrets = new ConfigurationSecrets(password, owner, protection);
        var instances = snapshot.Instances.Select(item =>
        {
            var previous = cloud.Instances.FirstOrDefault(value => value.Id == item.Id);
            var plain = original.Instances.FirstOrDefault(value => value.Id == item.Id);
            if (previous is not null && WorkspaceContent.Equal(new(1, [item], []), plain is null ? null : new(1, [plain], []))) return previous;
            var fields = registry.Types.Any(type => type.Id == item.TypeId)
                ? registry.GetProviders(item.TypeId).FirstOrDefault(value => value.Id == item.ProviderId)?.Fields : null;
            var publicKeys = (fields ?? []).Where(field => !field.IsSensitive && field.Kind != InstanceFieldKind.Secret)
                .Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
            var visible = item.Parameters.Where(pair => publicKeys.Contains(pair.Key)).ToDictionary();
            var hidden = item.Parameters.Where(pair => !publicKeys.Contains(pair.Key)).ToDictionary();
            if (item.HostingProviderId.Length > 0)
                hidden[InstanceHostingProviders.MetadataKey] = item.HostingProviderId;
            return new CloudInstanceConfiguration(item.Id, previous?.Version ?? "0", item.Name, item.TypeId, item.ProviderId,
                visible, secrets.Encrypt(item, hidden), item.CreatedAt, item.TagIds);
        }).ToArray();
        var tags = snapshot.Tags.Select(item => new CloudTagConfiguration(item,
            cloud.Tags.FirstOrDefault(value => value.Tag.Id == item.Id)?.Version ?? "0")).ToArray();
        return new(cloud.Revision, protection, instances, tags, cloud.UpdatedAt);
    }
}
