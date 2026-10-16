namespace Asgard.Hermod.UI.Controls;

/// <summary>数据库母组件补充的官方 Lucide 路径，与现有图标使用同一固定上游版本。</summary>
internal static partial class IconCatalog
{
    private static void AddDatabaseDesignIcons()
    {
        Geometries.TryAdd("message-square", Create("M22 17a2 2 0 0 1-2 2H6.828a2 2 0 0 0-1.414.586l-2.202 2.202A.71.71 0 0 1 2 21.286V5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2z"));
        Geometries.TryAdd("git-branch", Create("M15 6a9 9 0 0 0-9 9V3", "M15 6a3 3 0 1 0 6 0a3 3 0 1 0 -6 0", "M3 18a3 3 0 1 0 6 0a3 3 0 1 0 -6 0"));
        Geometries.TryAdd("history", Create("M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", "M3 3v5h5", "M12 7v5l4 2"));
        Geometries.TryAdd("save", Create("M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z", "M17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7", "M7 3v4a1 1 0 0 0 1 1h7"));
        Geometries.TryAdd("scan-search", Create("M3 7V5a2 2 0 0 1 2-2h2", "M17 3h2a2 2 0 0 1 2 2v2", "M21 17v2a2 2 0 0 1-2 2h-2", "M7 21H5a2 2 0 0 1-2-2v-2", "M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0", "m16 16-1.9-1.9"));
    }
}
