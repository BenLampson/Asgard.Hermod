namespace Asgard.Hermod.UI.Controls;

/// <summary>SSH 连接与工作页使用同一固定版本的官方 Lucide 图标。</summary>
internal static partial class IconCatalog
{
    /// <summary>转换原始 SVG 的路径、圆形与圆角矩形，不重新绘制图标。</summary>
    private static void AddSshIcons()
    {
        Geometries.Add("route", Create("M3 19A3 3 0 1 0 9 19A3 3 0 1 0 3 19Z", "M9 19h8.5a3.5 3.5 0 0 0 0-7h-11a3.5 3.5 0 0 1 0-7H15", "M15 5A3 3 0 1 0 21 5A3 3 0 1 0 15 5Z"));
        Geometries.Add("network", Create("M17 16H21A1 1 0 0 1 22 17V21A1 1 0 0 1 21 22H17A1 1 0 0 1 16 21V17A1 1 0 0 1 17 16Z", "M3 16H7A1 1 0 0 1 8 17V21A1 1 0 0 1 7 22H3A1 1 0 0 1 2 21V17A1 1 0 0 1 3 16Z", "M10 2H14A1 1 0 0 1 15 3V7A1 1 0 0 1 14 8H10A1 1 0 0 1 9 7V3A1 1 0 0 1 10 2Z", "M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3", "M12 12V8"));
        Geometries.Add("clipboard", Create("M9 2H15A1 1 0 0 1 16 3V5A1 1 0 0 1 15 6H9A1 1 0 0 1 8 5V3A1 1 0 0 1 9 2Z", "M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"));
        Geometries.Add("panel-right", Create("M5 3H19A2 2 0 0 1 21 5V19A2 2 0 0 1 19 21H5A2 2 0 0 1 3 19V5A2 2 0 0 1 5 3Z", "M15 3v18"));
        Geometries.Add("shield-check", Create("M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z", "m9 12 2 2 4-4"));
        Geometries.Add("chevron-up", Create("m18 15-6-6-6 6"));
    }
}
