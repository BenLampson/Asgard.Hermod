namespace Asgard.Hermod.UI.Controls;

/// <summary>Redis 内容页补充官方 Lucide 图标，几何与随项目保存的原始 SVG 一致。</summary>
internal static partial class IconCatalog
{
    private static void AddRedisIcons()
    {
        Geometries.Add("radar", Create("M19.07 4.93A10 10 0 0 0 6.99 3.34", "M4 6h.01", "M2.29 9.62A10 10 0 1 0 21.31 8.35", "M16.24 7.76A6 6 0 1 0 8.23 16.67", "M12 18h.01", "M17.99 11.66A6 6 0 0 1 15.77 16.67", "M10 12A2 2 0 1 0 14 12A2 2 0 1 0 10 12Z", "m13.41 10.59 5.66-5.66"));
        Geometries.Add("table-2", Create("M3 9h18", "M9 3v18", "M5 3H19A2 2 0 0 1 21 5V19A2 2 0 0 1 19 21H5A2 2 0 0 1 3 19V5A2 2 0 0 1 5 3Z"));
        Geometries.Add("fold-vertical", Create("M12 22v-6", "M12 8V2", "M4 12H2", "M10 12H8", "M16 12h-2", "M22 12h-2", "m15 19-3-3-3 3", "m15 5-3 3-3-3"));
        Geometries.Add("circle-help", Create("M2 12A10 10 0 1 0 22 12A10 10 0 1 0 2 12Z", "M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3", "M12 17h.01"));
    }
}
