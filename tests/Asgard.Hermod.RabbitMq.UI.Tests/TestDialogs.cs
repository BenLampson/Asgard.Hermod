namespace Asgard.Hermod.RabbitMq.UI.Tests;

internal sealed class TestDialogs : IRabbitMqDialogs
{
    internal RabbitMqForm? Last { get; private set; }
    internal Func<RabbitMqForm, Task<bool>>? OnShow { get; set; }
    internal string Copied { get; private set; } = "";
    internal byte[]? Exported { get; private set; }
    internal bool Preview { get; private set; }
    public Task<bool> ShowAsync(RabbitMqForm form) { Last = form; return OnShow?.Invoke(form) ?? Task.FromResult(false); }
    public void Copy(string content) => Copied = content;
    public Task ExportAsync(byte[] bytes, bool preview) { Exported = bytes; Preview = preview; return Task.CompletedTask; }
}
