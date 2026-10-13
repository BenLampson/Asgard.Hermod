namespace Asgard.Hermod.UI.Controls;

/// <summary>文件类型使用固定版本的官方 Lucide SVG，保留原始路径及圆角描边。</summary>
internal static partial class IconCatalog
{
    /// <summary>补充内置类型映射引用的图标，不依赖网络加载或系统文件关联。</summary>
    private static void AddFileTypeIcons()
    {
        Geometries.Add("video", Create("m16 13 5.223 3.482a.5.5 0 0 0 .777-.416V7.87a.5.5 0 0 0-.752-.432L16 10.5", "M4 6H14A2 2 0 0 1 16 8V16A2 2 0 0 1 14 18H4A2 2 0 0 1 2 16V8A2 2 0 0 1 4 6Z"));
        Geometries.Add("music", Create("M9 18V5l12-2v13", "M3 18A3 3 0 1 0 9 18A3 3 0 1 0 3 18Z", "M15 16A3 3 0 1 0 21 16A3 3 0 1 0 15 16Z"));
        Geometries.Add("file-image", Create("M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z", "M14 2v5a1 1 0 0 0 1 1h5", "M8 12A2 2 0 1 0 12 12A2 2 0 1 0 8 12Z", "m20 17-1.296-1.296a2.41 2.41 0 0 0-3.408 0L9 22"));
        Geometries.Add("pen-tool", Create("m18 13-.654-4.736a4 4 0 00-2.756-3.266l-9.163-2.9a2 2 0 00-2.017.493l-.82.819a2 2 0 00-.492 2.017l2.9 9.163a4 4 0 003.266 2.756L13 18", "M18.648 12.352a1.205 1.205 0 011.704 0l1.296 1.296a1.205 1.205 0 010 1.704l-6.296 6.296a1.205 1.205 0 01-1.704 0l-1.296-1.296a1.205 1.205 0 010-1.704z", "m3 3 6.586 6.586", "M9 11A2 2 0 1 0 13 11A2 2 0 1 0 9 11Z"));
        Geometries.Add("sheet", Create("M5 3H19A2 2 0 0 1 21 5V19A2 2 0 0 1 19 21H5A2 2 0 0 1 3 19V5A2 2 0 0 1 5 3Z", "M3 9L21 9", "M3 15L21 15", "M9 9L9 21", "M15 9L15 21"));
        Geometries.Add("presentation", Create("M2 3h20", "M21 3v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V3", "m7 21 5-5 5 5"));
        Geometries.Add("file-code", Create("M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z", "M14 2v5a1 1 0 0 0 1 1h5", "M10 12.5 8 15l2 2.5", "m14 12.5 2 2.5-2 2.5"));
        Geometries.Add("code", Create("m16 18 6-6-6-6", "m8 6-6 6 6 6"));
        Geometries.Add("braces", Create("M8 3H7a2 2 0 0 0-2 2v5a2 2 0 0 1-2 2 2 2 0 0 1 2 2v5c0 1.1.9 2 2 2h1", "M16 21h1a2 2 0 0 0 2-2v-5c0-1.1.9-2 2-2a2 2 0 0 1-2-2V5a2 2 0 0 0-2-2h-1"));
        Geometries.Add("type", Create("M12 4v16", "M4 7V5a1 1 0 0 1 1-1h14a1 1 0 0 1 1 1v2", "M9 20h6"));
        Geometries.Add("file-key", Create("M14 2v5a1 1 0 0 0 1 1h5", "M4 12v6", "M4 14h2", "M9.65 22H18a2 2 0 0 0 2-2V8a2.4 2.4 0 0 0-.706-1.706l-3.588-3.588A2.4 2.4 0 0 0 14 2H6a2 2 0 0 0-2 2v4", "M2 20A2 2 0 1 0 6 20A2 2 0 1 0 2 20Z"));
        Geometries.Add("disc-3", Create("M2 12A10 10 0 1 0 22 12A10 10 0 1 0 2 12Z", "M6 12c0-1.7.7-3.2 1.8-4.2", "M10 12A2 2 0 1 0 14 12A2 2 0 1 0 10 12Z", "M18 12c0 1.7-.7 3.2-1.8 4.2"));
    }
}
