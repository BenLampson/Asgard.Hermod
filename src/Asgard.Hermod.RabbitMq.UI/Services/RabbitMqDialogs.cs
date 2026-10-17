using Asgard.Hermod.RabbitMq.UI.ViewModels;
using Asgard.Hermod.RabbitMq.UI.Views;
using Microsoft.Win32;

namespace Asgard.Hermod.RabbitMq.UI.Services;

/// <summary>所有外部写入均由明确的用户按钮触发。</summary>
public sealed class RabbitMqDialogs(Func<Window?> owner) : IRabbitMqDialogs
{
    public Task<bool> ShowAsync(RabbitMqForm form) => Task.FromResult(new RabbitMqFormDialog(form) { Owner = owner() }.ShowDialog() == true);
    public void Copy(string content) => Clipboard.SetText(content);
    public async Task ExportAsync(byte[] bytes, bool preview)
    {
        var picker = new SaveFileDialog
        {
            Title = Text.Get(preview ? "RabbitMqExportPreview" : "RabbitMqExportMessage"),
            FileName = preview ? "message-preview.bin" : "message.bin",
            Filter = Text.Get("RabbitMqExportFilter"),
            AddExtension = true,
            DefaultExt = ".bin"
        };
        if (picker.ShowDialog(owner()) == true) await File.WriteAllBytesAsync(picker.FileName, bytes);
    }
}
