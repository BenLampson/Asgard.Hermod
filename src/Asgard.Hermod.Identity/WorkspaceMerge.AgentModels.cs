namespace Asgard.Hermod.Identity;

/// <summary>连接地址、模型与密钥作为整体合并，避免把一个地址与另一个地址的密钥拼在一起。</summary>
public sealed partial class WorkspaceMerge
{
    private AgentModelSet MergeAgentModels(AgentModelSet? baseline, AgentModelSet local, AgentModelSet cloud)
    {
        baseline ??= AgentModelSet.Empty;
        var profiles = MergeItems(baseline.Profiles, local.Profiles, cloud.Profiles, profile => profile.Id, profile => profile.Name,
            MergeAgentProfile, (left, right) => left == right, "agent-model");
        string selected = Field("agent-model:default", Text.Get("AgentModelDefault"), "AgentModelDefault", baseline.DefaultProfileId, local.DefaultProfileId, cloud.DefaultProfileId) ?? "";
        // 删除默认配置同时撤销默认引用，不因为旧设备的选择复活已删除的凭据。
        if (profiles.All(profile => profile.Id != selected)) selected = "";
        return new(profiles, selected);
    }
    private AgentModelProfile MergeAgentProfile(AgentModelProfile baseline, AgentModelProfile local, AgentModelProfile cloud)
    {
        static bool SameConnection(AgentModelProfile left, AgentModelProfile right) => left.Endpoint == right.Endpoint && left.Model == right.Model && left.ApiKey == right.ApiKey;
        string prefix = "agent-model:" + local.Id + ":";
        var connection = SameConnection(local, cloud) ? local : SameConnection(local, baseline) ? cloud : SameConnection(cloud, baseline) ? local
            : Choose(prefix + "connection", local.Name, "AgentModelConnection", local, cloud, Text.Get("SyncSecretValue"), Text.Get("SyncSecretValue"))!;
        return local with
        {
            Name = Field(prefix + "name", local.Name, "AgentModelProfileName", baseline.Name, local.Name, cloud.Name)!,
            Endpoint = connection.Endpoint,
            Model = connection.Model,
            ApiKey = connection.ApiKey,
            UpdatedAt = local.UpdatedAt > cloud.UpdatedAt ? local.UpdatedAt : cloud.UpdatedAt,
        };
    }
}
