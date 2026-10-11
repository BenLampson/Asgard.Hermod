namespace Asgard.Hermod.UI.Models;

/// <summary>同名对象的处理方式，默认要求逐项确认覆盖。</summary>
public enum ObjectStorageOverwritePolicy
{
    Ask,
    Skip,
    Overwrite,
}
