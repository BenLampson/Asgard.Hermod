namespace Asgard.Hermod.UI.Models;

/// <summary>多个选中标签路径之间的匹配关系。</summary>
public enum TagFilterMatchMode
{
    /// <summary>实例命中任意一个选中路径即可显示。</summary>
    Any,

    /// <summary>实例必须分别命中每个选中路径。</summary>
    All
}
