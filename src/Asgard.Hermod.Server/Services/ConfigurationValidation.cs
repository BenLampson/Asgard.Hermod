using System.Text;
using System.Text.Json;

namespace Asgard.Hermod.Server;

/// <summary>验证明细大小、关系和密文结构，不解密用户秘密。</summary>
internal static class ConfigurationValidation
{
    /// <summary>先验证全部内容，失败时仓储尚未修改。</summary>
    public static void Validate(ConfigurationUpload upload)
    {
        if (upload is null || !Version(upload.ExpectedRevision) || upload.Configuration is not { } configuration
            || configuration.Instances is null || configuration.Tags is null || configuration.LegacyWorkspace is not null
            || configuration.Instances.Count > 10000 || configuration.Tags.Count > 10000
            || configuration.Instances.Any(item => item is null) || configuration.Tags.Any(item => item?.Tag is null)
            || JsonSerializer.SerializeToUtf8Bytes(configuration).Length > 7 * 1024 * 1024) Invalid();
        var value = upload!.Configuration;
        WorkspaceEncryption.ValidateEnvelope(value.Protection!);
        if (value.Instances.Select(item => item.Id).Distinct().Count() != value.Instances.Count
            || value.Tags.Select(item => item.Tag.Id).Distinct().Count() != value.Tags.Count) Invalid();
        var ids = value.Tags.Select(item => item.Tag.Id).ToHashSet(StringComparer.Ordinal);
        var tagLookup = value.Tags.ToDictionary(item => item.Tag.Id, item => item.Tag, StringComparer.Ordinal);
        var siblingNames = new HashSet<(string?, string)>();
        foreach (var item in value.Instances)
        {
            if (!Id(item.Id) || !Id(item.TypeId) || !Id(item.ProviderId) || !Version(item.Version)
                || !Id(item.Name) || item.CreatedAt.Kind != DateTimeKind.Utc || item.Parameters is null
                || item.Parameters.Count > 100 || item.Parameters.Any(pair => !Id(pair.Key) || pair.Value is null || pair.Value.Length > 65536)
                || item.TagIds is null || item.TagIds.Count > ids.Count || item.TagIds.Any(tag => !ids.Contains(tag))
                || item.TagIds.Distinct().Count() != item.TagIds.Count) Invalid();
            WorkspaceEncryption.ValidateEnvelope(item.Secrets);
            if (item.Secrets.Salt != value.Protection!.Salt) Invalid();
        }
        foreach (var item in value.Tags)
        {
            var tag = item.Tag;
            if (!Id(tag.Id) || !Version(item.Version) || !Id(tag.Name) || tag.Color is not { Length: 7 }
                || tag.Color[0] != '#' || !tag.Color.Skip(1).All(Uri.IsHexDigit)
                || tag.ParentId is not null && !ids.Contains(tag.ParentId)) Invalid();
            if (tag.Name != tag.Name.Trim().Normalize(NormalizationForm.FormC) || tag.Name.Length > 80 || tag.Name.Contains('/')
                || !siblingNames.Add((tag.ParentId, tag.Name.Normalize(NormalizationForm.FormC).ToUpperInvariant()))
                || !tag.IsBuiltIn && (tag.Id.StartsWith("builtin.", StringComparison.Ordinal)
                    || tag.ParentId is null && tag.Name.Equals("Environment", StringComparison.OrdinalIgnoreCase)
                    || tag.ParentId is not null && tagLookup[tag.ParentId].IsBuiltIn)) Invalid();
            if (tag.IsBuiltIn && tag != BuiltIn(tag.Id)) Invalid();
            var visited = new HashSet<string>(StringComparer.Ordinal) { tag.Id }; string? parent = tag.ParentId;
            while (parent is not null)
            {
                if (!visited.Add(parent)) Invalid();
                parent = tagLookup[parent].ParentId;
            }
        }
    }
    /// <summary>协议标识不允许控制字符。</summary>
    private static bool Id(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl);
    private static bool Version(string? value) => Id(value) && value!.Length <= 64;
    /// <summary>系统标签不可由用户编辑；协议使用固定名称。</summary>
    private static SyncedTag? BuiltIn(string id) => id switch
    {
        "builtin.environment.production" => new(id, "Production", null, "#EF4444", true),
        "builtin.environment.staging" => new(id, "Staging", null, "#F59E0B", true),
        "builtin.environment.development" => new(id, "Development", null, "#22C55E", true),
        _ => null,
    };
    /// <summary>服务器只返回安全错误码。</summary>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid() => throw new HermodApiException(400, "SyncSnapshotInvalid");
}
