using System.Globalization;
using System.Text;

namespace Asgard.Hermod.Mcp;

/// <summary>禁止规则始终按稳定实例标识和协议原名匹配，不使用翻译后的标签。</summary>
internal static class McpExclusionRules
{
    internal const int Maximum = 128;

    internal static void Validate(McpExclusion exclusion)
    {
        if (string.IsNullOrWhiteSpace(exclusion.InstanceId) || exclusion.InstanceId.Length > 128 || exclusion.InstanceId.Any(char.IsControl)
            || !McpGrantRules.Supports(exclusion.TypeId) || exclusion.Resource.Length > 253
            || exclusion.Qualifier.Length > 1024 || exclusion.Table.Length > 128
            || new[] { exclusion.Resource, exclusion.Qualifier, exclusion.Table }.Any(value => value.Any(char.IsControl))
            || exclusion.Resource.Length == 0 && (exclusion.Qualifier.Length > 0 || exclusion.Table.Length > 0)
            || exclusion.TypeId is not ("storage" or "redis" or "postgresql") && exclusion.Qualifier.Length > 0
            || exclusion.TypeId is not ("mysql" or "postgresql" or "rabbitmq") && exclusion.Table.Length > 0
            || exclusion.TypeId == "postgresql" && exclusion.Table.Length > 0 && exclusion.Qualifier.Length == 0
            || exclusion.TypeId == "storage" && (!McpGrantRules.IsObjectPath(exclusion.Qualifier)
                || exclusion.Resource.Length > 0 && (!char.IsAsciiLetterOrDigit(exclusion.Resource[0])
                    || !char.IsAsciiLetterOrDigit(exclusion.Resource[^1])
                    || exclusion.Resource.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not ('.' or '-'))))
            || exclusion.TypeId == "redis" && exclusion.Resource.Length > 0
                && (!int.TryParse(exclusion.Resource, NumberStyles.None, CultureInfo.InvariantCulture, out int database) || database < 0)
            || exclusion.TypeId == "postgresql" && new[] { exclusion.Resource, exclusion.Qualifier, exclusion.Table }.Any(value => Encoding.UTF8.GetByteCount(value) > 63)
            || exclusion.TypeId == "ssh" && exclusion.Resource.Length > 0 && !McpGrantRules.IsServiceName(exclusion.Resource))
            throw Text.Exception("McpInvalidExclusion");
    }

    /// <summary>精确检查一个资源或子项；includeDescendants 用于递归操作的范围交集检查。</summary>
    internal static bool Blocks(McpLocalPolicy policy, McpTarget target, string resource = "", string qualifier = "", string table = "",
        bool includeDescendants = false)
    {
        foreach (var exclusion in policy.Exclusions ?? [])
        {
            if (exclusion.InstanceId != target.Id) continue;
            if (exclusion.Resource.Length == 0) return true;
            if (exclusion.TypeId != target.TypeId) continue;
            if (resource.Length == 0) { if (includeDescendants) return true; continue; }
            if (exclusion.Resource != resource) continue;
            if (exclusion.Qualifier.Length == 0 && exclusion.Table.Length == 0) return true;
            if (target.TypeId is "storage" or "redis")
            {
                if (qualifier.StartsWith(exclusion.Qualifier, StringComparison.Ordinal)
                    || includeDescendants && exclusion.Qualifier.StartsWith(qualifier, StringComparison.Ordinal)) return true;
                continue;
            }
            if (target.TypeId == "postgresql" && exclusion.Qualifier.Length > 0)
            {
                if (qualifier.Length == 0) { if (includeDescendants) return true; continue; }
                if (exclusion.Qualifier != qualifier) continue;
            }
            if (exclusion.Table.Length == 0) return true;
            if (table.Length == 0) { if (includeDescendants) return true; continue; }
            if (exclusion.Table == table) return true;
        }
        return false;
    }
}
