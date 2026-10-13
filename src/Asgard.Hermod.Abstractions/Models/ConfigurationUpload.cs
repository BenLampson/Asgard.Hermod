namespace Asgard.Hermod.Abstractions.Models;

/// <summary>提交合并后的完整集合，集合版本与条目版本均必须匹配。</summary>
public sealed record ConfigurationUpload(string ExpectedRevision, CloudConfiguration Configuration);
