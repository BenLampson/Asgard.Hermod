namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

public sealed partial class RabbitMqWorkspaceViewModel
{
    private bool _getUnknown;
    private DateTimeOffset? _snapshotAt;
    public ObservableCollection<RabbitMqMessageRow> Messages { get; } = [];
    public RabbitMqMessageRow? SelectedMessage { get; set { if (SetProperty(ref field, value)) RefreshState(); } }
    public string GetCount { get; set { if (SetProperty(ref field, value)) GetCommand?.NotifyCanExecuteChanged(); } } = "10";
    public string GetMode { get; set => SetProperty(ref field, value); } = "requeue";
    public string MessageEncoding { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(Payload)); } } = "auto";
    public IReadOnlyList<InstanceFieldChoice> GetModes { get; } = [new("requeue", Text.Get("RabbitMqRequeue")), new("remove", Text.Get("RabbitMqRemoveMessages"))];
    public IReadOnlyList<InstanceFieldChoice> MessageEncodings { get; } = [new("auto", Text.Get("RabbitMqAutomaticEncoding")), new("base64", "Base64")];
    public string Payload => SelectedMessage is null ? "" : MessageEncoding == "base64" ? Convert.ToBase64String(SelectedMessage.Message.Payload) : SelectedMessage.Message.Display();
    public string Properties => SelectedMessage is null ? "" : Pretty(SelectedMessage.Message.Properties);
    public string Headers => SelectedMessage is not null && SelectedMessage.Message.Properties.TryGetProperty("headers", out var headers) ? Pretty(headers) : "";
    public string SnapshotText => _snapshotAt is null ? Text.Get("RabbitMqNoSnapshot") : Text.Format("RabbitMqSnapshot", _snapshotAt.Value.ToLocalTime().ToString("T", CultureInfo.CurrentCulture), Messages.Count);
    public bool IsTruncated => SelectedMessage?.Message.IsTruncated == true;
    public string TruncationText => IsTruncated ? Text.Format("RabbitMqTruncated", SelectedMessage!.Message.Payload.Length, SelectedMessage.Message.OriginalBytes) : "";
    public string CopyLabel => Text.Get(IsTruncated ? "RabbitMqCopyPreview" : "RabbitMqCopyMessage");
    public string ExportLabel => Text.Get(IsTruncated ? "RabbitMqExportPreview" : "RabbitMqExportMessage");
    private Task OpenMessagesAsync()
    {
        if (QueueTarget is not { } queue) return Task.CompletedTask;
        _read?.Cancel(); _revision++; _detail = queue; _detailName = queue.Name; _inMessages = true; _inDetail = true;
        _snapshotAt = null; _getUnknown = false; Messages.Clear(); SelectedMessage = null; RefreshState(); return Task.CompletedTask;
    }
    private async Task GetAsync()
    {
        if (SelectedVhost is null || QueueTarget is null) return;
        string host = SelectedVhost, queue = _detailName;
        int count = int.Parse(GetCount, CultureInfo.InvariantCulture); bool remove = GetMode == "remove";
        async Task Fetch()
        {
            IsBusy = true; Error = ""; RefreshState();
            try
            {
                var messages = await _session!.GetMessagesAsync(host, queue, count, remove, _lifetime.Token);
                Messages.Clear(); int number = 0; foreach (var message in messages) Messages.Add(new(++number, message));
                SelectedMessage = Messages.FirstOrDefault(); _snapshotAt = DateTimeOffset.Now;
                Notice = Text.Get(remove ? "RabbitMqMessagesRemoved" : "RabbitMqMessagesRequeued");
            }
            catch (Exception error) { _getUnknown = error is RabbitMqFailure { OutcomeUnknown: true }; Fail(error); throw; }
            finally { IsBusy = false; RefreshState(); }
        }
        if (remove)
            await ShowFormAsync(new(RabbitMqOperation.PurgeQueue, host, queue, WriteTarget, _ => Fetch(), confirmation: true,
                titleKey: "RabbitMqRemoveMessages", impactKey: "RabbitMqRemoveMessagesImpact"));
        else await Fetch();
    }
    private void CopyMessage()
    {
        try { _dialogs.Copy(Payload); Notice = Text.Get(IsTruncated ? "RabbitMqPreviewCopied" : "RabbitMqCopied"); RefreshState(); }
        catch (Exception error) { Fail(error); }
    }
    private Task ExportMessageAsync() => SelectedMessage is null ? Task.CompletedTask : _dialogs.ExportAsync(SelectedMessage.Message.Payload.ToArray(), IsTruncated);
}
