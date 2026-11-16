namespace Asgard.Hermod.ObjectStorage.Abstractions.Models;

/// <summary>从已确认的选择展开的对象键清单；执行阶段不再枚举或扩大范围。</summary>
public sealed record ObjectStorageDeletePlan(IReadOnlyList<string> Keys);
