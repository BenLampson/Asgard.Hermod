using System.Text.Json;

namespace Asgard.Hermod.Abstractions.Models;

/// <summary>忽略集合排列比较业务内容；密文随机数和设备保护不参与判断。</summary>
public static class WorkspaceContent
{
    /// <summary>规范化排序后比较，不以密文或时间判断版本先后。</summary>
    public static bool Equal(WorkspaceSnapshot? left, WorkspaceSnapshot? right) => Canonical(left) == Canonical(right);
    /// <summary>规范化仅在内存内使用，禁止写日志。</summary>
    public static string Canonical(WorkspaceSnapshot? value) => JsonSerializer.Serialize(value is null ? null : value with
    {
        Instances = value.Instances.OrderBy(item => item.Id, StringComparer.Ordinal).Select(item => item with
        {
            Parameters = item.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(),
            TagIds = item.TagIds.Order(StringComparer.Ordinal).ToArray(),
        }).ToArray(),
        Tags = value.Tags.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
    });
}
