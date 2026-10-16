namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

/// <summary>窗口草稿由本模型拥有，错误不关闭窗口；结果未知后同一草稿禁止再次提交。</summary>
public sealed partial class RabbitMqForm : ObservableObject
{
    private readonly Func<RabbitMqMutation, Task> _submit;
    private readonly RabbitMqRecord? _existing;
    public RabbitMqForm(RabbitMqOperation operation, string vhost, string name, string target, Func<RabbitMqMutation, Task> submit,
        RabbitMqRecord? existing = null, bool confirmation = false, string? titleKey = null, string? impactKey = null)
    {
        Operation = operation; Vhost = vhost; Name = name; Target = target; _submit = submit; _existing = existing;
        IsConfirmation = confirmation; Title = Text.Get(titleKey ?? "RabbitMq" + operation);
        Impact = Text.Get(impactKey ?? (confirmation ? "RabbitMq" + operation + "Impact" : "RabbitMq" + operation + "Hint"));
        BuildFields();
        foreach (var item in Fields) item.PropertyChanged += OnFieldChanged;
        SubmitCommand = new(_ => SubmitAsync(), _ => !IsBusy && !OutcomeUnknown && (!IsConfirmation || Confirmation == Name));
        AddArgumentCommand = new(_ => Arguments.Add(new()), _ => !IsBusy);
        RemoveArgumentCommand = new(value => { if (value is RabbitMqArgument item) Arguments.Remove(item); }, _ => !IsBusy);
    }
    public event EventHandler? Completed;
    public RabbitMqOperation Operation { get; }
    public string Vhost { get; }
    public string Name { get; }
    public string Target { get; }
    public string Title { get; }
    public string Impact { get; }
    public bool IsConfirmation { get; }
    public bool IsBusy { get; private set; }
    public bool OutcomeUnknown { get; private set; }
    public bool IsEditable => !IsBusy && !OutcomeUnknown;
    public bool CanClose => !IsBusy;
    public bool IsDestructive => IsConfirmation;
    public string Error { get; private set; } = "";
    public bool HasError => Error.Length > 0;
    public string Confirmation { get; set { if (SetProperty(ref field, value)) SubmitCommand.NotifyCanExecuteChanged(); } } = "";
    public string ConfirmationLabel => Text.Format("RabbitMqTypeToConfirm", Name);
    public ObservableCollection<RabbitMqDraftField> Fields { get; } = [];
    /// <summary>文本字段按原稿纵向排列，布尔选项在独立双列表单中呈现。</summary>
    public IEnumerable<RabbitMqDraftField> InputFields => Fields.Where(item => item.Kind != "boolean");
    /// <summary>布尔选项独立分组，避免重复显示字段名称。</summary>
    public IEnumerable<RabbitMqDraftField> BooleanFields => Fields.Where(item => item.Kind == "boolean");
    /// <summary>普通表单显示作用域，危险操作展示完整目标信息。</summary>
    public string TargetLabel => IsConfirmation ? Target : Vhost;
    public ObservableCollection<RabbitMqArgument> Arguments { get; } = [];
    public bool HasArguments => !IsConfirmation && Operation is RabbitMqOperation.DeclareQueue or RabbitMqOperation.DeclareExchange or RabbitMqOperation.CreateBinding or RabbitMqOperation.PutPolicy or RabbitMqOperation.Publish;
    public string ArgumentsLabel => Text.Get(Operation == RabbitMqOperation.Publish ? "RabbitMqHeaders" : Operation == RabbitMqOperation.PutPolicy ? "RabbitMqDefinition" : "RabbitMqArguments");
    public double Width => Operation == RabbitMqOperation.Publish ? 800 : Operation == RabbitMqOperation.PutPolicy ? 680 : 640;
    public double Height => IsConfirmation ? 560 : 760;
    public AsyncRelayCommand SubmitCommand { get; }
    public RelayCommand AddArgumentCommand { get; }
    public RelayCommand RemoveArgumentCommand { get; }
    private string Value(string key) => Fields.FirstOrDefault(item => item.Key == key)?.Value ?? "";
    private bool Flag(string key) => Value(key) == "true";
    private void OnFieldChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is RabbitMqDraftField { Key: "type", Value: "quorum" or "stream" } && Operation == RabbitMqOperation.DeclareQueue)
        { Fields.First(item => item.Key == "durable").Checked = true; Fields.First(item => item.Key == "auto_delete").Checked = false; }
    }
    private async Task SubmitAsync()
    {
        try
        {
            Error = ""; var mutation = BuildMutation(); RabbitMqValidation.Validate(mutation);
            IsBusy = true; Refresh(); await _submit(mutation); Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) { Error = Text.Error(error, "RabbitMqRequestFailed"); OutcomeUnknown = error is RabbitMqFailure { OutcomeUnknown: true }; }
        finally { IsBusy = false; Refresh(); }
    }
    private void Refresh()
    {
        foreach (string name in new[] { nameof(IsBusy), nameof(IsEditable), nameof(CanClose), nameof(Error), nameof(HasError), nameof(OutcomeUnknown) }) OnPropertyChanged(name);
        SubmitCommand.NotifyCanExecuteChanged(); AddArgumentCommand.NotifyCanExecuteChanged(); RemoveArgumentCommand.NotifyCanExecuteChanged();
    }
    private void Field(string key, string resource, string value = "", string kind = "text", bool required = false, bool readOnly = false) => Fields.Add(new(key, resource, value, kind, required) { IsReadOnly = readOnly });
    private void Choice(string key, string resource, string value, params string[] choices) => Fields.Add(new(key, resource, value, "choice")
    { Choices = choices.Select(item => new InstanceFieldChoice(item, Text.Get("RabbitMqChoice" + item.Replace("-", "", StringComparison.Ordinal)))).ToArray() });
}
