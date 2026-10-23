namespace Asgard.Hermod.Abstractions.Services;

/// <summary>商店可用性与 GitHub 发布元数据分离，避免把尚未上架的版本当作商店更新。</summary>
public sealed record ApplicationUpdateResult(bool HasUpdate, PublishedApplicationRelease? Release);
