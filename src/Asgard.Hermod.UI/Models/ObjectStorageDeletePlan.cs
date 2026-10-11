namespace Asgard.Hermod.UI.Models;

/// <summary>先枚举目录范围，再确认这份明确的对象键清单；执行阶段不再扩展范围。</summary>
public sealed record ObjectStorageDeletePlan(IReadOnlyList<string> Keys);
