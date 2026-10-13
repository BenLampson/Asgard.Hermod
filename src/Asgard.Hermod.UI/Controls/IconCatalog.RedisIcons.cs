namespace Asgard.Hermod.UI.Controls;

/// <summary>Redis 内容页补充官方 Lucide 图标，几何与随项目保存的原始 SVG 一致。</summary>
internal static partial class IconCatalog
{
    private static void AddRedisIcons()
    {
        Geometries.Add("table-2", Create("M3 9h18", "M9 3v18", "M5 3H19A2 2 0 0 1 21 5V19A2 2 0 0 1 19 21H5A2 2 0 0 1 3 19V5A2 2 0 0 1 5 3Z"));
        Geometries.Add("fold-vertical", Create("M12 22v-6", "M12 8V2", "M4 12H2", "M10 12H8", "M16 12h-2", "M22 12h-2", "m15 19-3-3-3 3", "m15 5-3 3-3-3"));
        Geometries.Add("circle-help", Create("M2 12A10 10 0 1 0 22 12A10 10 0 1 0 2 12Z", "M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3", "M12 17h.01"));
    }
}
