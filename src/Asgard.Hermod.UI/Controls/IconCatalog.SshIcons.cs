namespace Asgard.Hermod.UI.Controls;

/// <summary>SSH 连接与工作页使用同一固定版本的官方 Lucide 图标。</summary>
internal static partial class IconCatalog
{
    /// <summary>转换原始 SVG 的路径、圆形与圆角矩形，不重新绘制图标。</summary>
    private static void AddSshIcons()
    {
        Geometries.TryAdd("arrow-down-up", Create("m3 16 4 4 4-4", "M7 20V4", "m21 8-4-4-4 4", "M17 4v16"));
        Geometries.TryAdd("arrow-up", Create("m5 12 7-7 7 7", "M12 19V5"));
        Geometries.TryAdd("pause", Create("M15 3H18A1 1 0 0 1 19 4V20A1 1 0 0 1 18 21H15A1 1 0 0 1 14 20V4A1 1 0 0 1 15 3Z", "M6 3H9A1 1 0 0 1 10 4V20A1 1 0 0 1 9 21H6A1 1 0 0 1 5 20V4A1 1 0 0 1 6 3Z"));
        Geometries.TryAdd("unplug", Create("m19 5 3-3", "m2 22 3-3", "M6.3 20.3a2.4 2.4 0 0 0 3.4 0L12 18l-6-6-2.3 2.3a2.4 2.4 0 0 0 0 3.4Z", "M7.5 13.5 10 11", "M10.5 16.5 13 14", "m12 6 6 6 2.3-2.3a2.4 2.4 0 0 0 0-3.4l-2.6-2.6a2.4 2.4 0 0 0-3.4 0Z"));
        Geometries.Add("route", Create("M3 19A3 3 0 1 0 9 19A3 3 0 1 0 3 19Z", "M9 19h8.5a3.5 3.5 0 0 0 0-7h-11a3.5 3.5 0 0 1 0-7H15", "M15 5A3 3 0 1 0 21 5A3 3 0 1 0 15 5Z"));
        Geometries.Add("network", Create("M17 16H21A1 1 0 0 1 22 17V21A1 1 0 0 1 21 22H17A1 1 0 0 1 16 21V17A1 1 0 0 1 17 16Z", "M3 16H7A1 1 0 0 1 8 17V21A1 1 0 0 1 7 22H3A1 1 0 0 1 2 21V17A1 1 0 0 1 3 16Z", "M10 2H14A1 1 0 0 1 15 3V7A1 1 0 0 1 14 8H10A1 1 0 0 1 9 7V3A1 1 0 0 1 10 2Z", "M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3", "M12 12V8"));
        Geometries.Add("clipboard", Create("M9 2H15A1 1 0 0 1 16 3V5A1 1 0 0 1 15 6H9A1 1 0 0 1 8 5V3A1 1 0 0 1 9 2Z", "M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"));
        Geometries.Add("panel-right", Create("M5 3H19A2 2 0 0 1 21 5V19A2 2 0 0 1 19 21H5A2 2 0 0 1 3 19V5A2 2 0 0 1 5 3Z", "M15 3v18"));
        Geometries.Add("shield-check", Create("M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z", "m9 12 2 2 4-4"));
        Geometries.Add("chevron-up", Create("m18 15-6-6-6 6"));
        Geometries.Add("wrench", Create("M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.106-3.105c.32-.322.863-.22.983.218a6 6 0 0 1-8.259 7.057l-7.91 7.91a1 1 0 0 1-2.999-3l7.91-7.91a6 6 0 0 1 7.057-8.259c.438.12.54.662.219.984z"));
        Geometries.Add("eraser", Create("M21 21H8a2 2 0 0 1-1.42-.587l-3.994-3.999a2 2 0 0 1 0-2.828l10-10a2 2 0 0 1 2.829 0l5.999 6a2 2 0 0 1 0 2.828L12.834 21", "m5.082 11.09 8.828 8.828"));
    }
}
