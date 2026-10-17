using System.Windows.Controls.Primitives;

namespace Asgard.Hermod.ObjectStorage.UI.Controls;

/// <summary>列表与详情复用对象操作菜单，并明确绑定本次打开菜单的对象。</summary>
internal static class ObjectStorageMenuFactory
{
    /// <summary>菜单尺寸对应 Pencil；业务可用性由命令控制，按钮菜单与右键采用各自的锚点。</summary>
    internal static ContextMenu Create(ObjectStorageWorkspaceViewModel model, ObjectStorageEntryViewModel row,
        FrameworkElement target, bool includeDownload = true, PlacementMode placement = PlacementMode.Bottom)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = placement, Width = 244 };
        if (!row.IsDirectory)
        {
            menu.Items.Add(Item(Text.Get("OpenFile"), "folder-open", model.OpenFileCommand, row));
            menu.Items.Add(Item(Text.Get("PreviewFile"), "file-text", model.PreviewFileCommand, row));
            menu.Items.Add(new Separator());
        }
        if (includeDownload) menu.Items.Add(Item(Text.Get("Download"), "download", model.DownloadCommand, row));
        menu.Items.Add(Item(Text.Get("CopyObjectKey"), "copy", model.CopyKeyCommand, row));
        menu.Items.Add(Item(Text.Get("GenerateTemporaryLink2"), "link", model.TemporaryLinkCommand, row));
        menu.Items.Add(new Separator());
        var delete = Item(Text.Get("DeleteObjects3"), "trash-2", model.DeleteCommand, row);
        delete.SetResourceReference(Control.ForegroundProperty, "DangerBrush");
        menu.Items.Add(delete);
        target.ContextMenu = menu;
        return menu;
    }

    /// <summary>图标继承菜单项的前景色，使危险操作和禁用状态同步作用于图标与标题。</summary>
    private static MenuItem Item(string label, string icon, ICommand command, ObjectStorageEntryViewModel row) => new()
    {
        Header = label,
        Command = command,
        CommandParameter = row,
        Icon = new IconView { Kind = icon, Width = 15, Height = 15 }
    };
}
