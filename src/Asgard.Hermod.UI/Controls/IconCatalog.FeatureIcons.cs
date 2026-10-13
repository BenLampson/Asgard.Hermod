namespace Asgard.Hermod.UI.Controls;

/// <summary>系统设置和实例向导使用的官方 Lucide 图标，来源与主目录相同。</summary>
internal static partial class IconCatalog
{
    /// <summary>统一补充新界面所需图标；保留设计稿使用的旧别名。</summary>
    static IconCatalog()
    {
        Geometries.Add("tags", Create(
            "M13.172 2a2 2 0 0 1 1.414.586l6.71 6.71a2.4 2.4 0 0 1 0 3.408l-4.592 4.592a2.4 2.4 0 0 1-3.408 0l-6.71-6.71A2 2 0 0 1 6 9.172V3a1 1 0 0 1 1-1z",
            "M2 7v6.172a2 2 0 0 0 .586 1.414l6.71 6.71a2.4 2.4 0 0 0 3.191.193",
            "M10 6.5A.5 .5 0 1 0 11 6.5A.5 .5 0 1 0 10 6.5Z"));
        Geometries.Add("palette", Create(
            "M12 22a1 1 0 0 1 0-20 10 9 0 0 1 10 9 5 5 0 0 1-5 5h-2.25a1.75 1.75 0 0 0-1.4 2.8l.3.4a1.75 1.75 0 0 1-1.4 2.8z",
            "M13 6.5A.5 .5 0 1 0 14 6.5A.5 .5 0 1 0 13 6.5Z",
            "M17 10.5A.5 .5 0 1 0 18 10.5A.5 .5 0 1 0 17 10.5Z",
            "M6 12.5A.5 .5 0 1 0 7 12.5A.5 .5 0 1 0 6 12.5Z",
            "M8 7.5A.5 .5 0 1 0 9 7.5A.5 .5 0 1 0 8 7.5Z"));
        Geometries.Add("hard-drive", Create("M10 16h.01",
            "M2.212 11.577a2 2 0 0 0-.212.896V18a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-5.527a2 2 0 0 0-.212-.896L18.55 5.11A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z",
            "M21.946 12.013H2.054", "M6 16h.01"));
        Geometries.Add("user-round", Create("M7 8A5 5 0 1 0 17 8A5 5 0 1 0 7 8Z", "M20 21a8 8 0 0 0-16 0"));
        Geometries.Add("keyboard", Create("M10 8h.01", "M12 12h.01", "M14 8h.01", "M16 12h.01",
            "M18 8h.01", "M6 8h.01", "M7 16h10", "M8 12h.01",
            "M4 4H20A2 2 0 0 1 22 6V18A2 2 0 0 1 20 20H4A2 2 0 0 1 2 18V6A2 2 0 0 1 4 4Z"));
        Geometries.Add("info", Create("M2 12A10 10 0 1 0 22 12A10 10 0 1 0 2 12Z", "M12 16v-4", "M12 8h.01"));
        Geometries.Add("lock-keyhole", Create("M11 16A1 1 0 1 0 13 16A1 1 0 1 0 11 16Z",
            "M5 10H19A2 2 0 0 1 21 12V20A2 2 0 0 1 19 22H5A2 2 0 0 1 3 20V12A2 2 0 0 1 5 10Z",
            "M7 10V7a5 5 0 0 1 10 0v3"));
        Geometries.Add("corner-down-right", Create("m15 10 5 5-5 5", "M4 4v7a4 4 0 0 0 4 4h12"));
        Geometries.Add("ellipsis", Create("M11 12A1 1 0 1 0 13 12A1 1 0 1 0 11 12Z",
            "M18 12A1 1 0 1 0 20 12A1 1 0 1 0 18 12Z", "M4 12A1 1 0 1 0 6 12A1 1 0 1 0 4 12Z"));
        Geometries.Add("pencil", Create(
            "M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z", "m15 5 4 4"));
        Geometries.Add("trash-2", Create("M10 11v6", "M14 11v6",
            "M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6", "M3 6h18",
            "M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"));
        Geometries.Add("pipette", Create("m12 9-8.414 8.414A2 2 0 0 0 3 18.828v1.344a2 2 0 0 1-.586 1.414A2 2 0 0 1 3.828 21h1.344a2 2 0 0 0 1.414-.586L15 12",
            "m18 9 .4.4a1 1 0 1 1-3 3l-3.8-3.8a1 1 0 1 1 3-3l.4.4 3.4-3.4a1 1 0 1 1 3 3z", "m2 22 .414-.414"));
        Geometries.Add("sliders-horizontal", Create("M10 5H3", "M12 19H3", "M14 3v4", "M16 17v4",
            "M21 12h-9", "M21 19h-5", "M21 5h-7", "M8 10v4", "M8 12H3"));
        Geometries.Add("cloud", Create("M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z"));
        Geometries.Add("arrow-right", Create("M5 12h14", "m12 5 7 7-7 7"));
        Geometries.Add("arrow-left", Create("m12 19-7-7 7-7", "M19 12H5"));
        Geometries.Add("folder", Create("M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"));
        AddObjectStorageIcons();
        AddRedisIcons();
        AddSshIcons();
    }
}
