using Asgard.Hermod.Redis.UI.ViewModels;

namespace Asgard.Hermod.Redis.UI.Views;

/// <summary>收集最小初始值，原子创建及权限校验由协议模块完成。</summary>
public partial class RedisCreateDialog : HermodDialog
{
    /// <summary>真实类型名不翻译，控件文字来自共享资源。</summary>
    public RedisCreateDialog()
    {
        InitializeComponent(); TypeInput.ItemsSource = new[] { "string", "hash", "list", "set", "zset", "stream" };
        TypeInput.SelectedItem = "string"; Loaded += (_, _) => KeyInput.Focus();
    }
    /// <summary>未接受窗口时没有创建请求。</summary>
    public RedisCreateRequest? Request { get; private set; }
    private void OnTypeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (FieldControl is null || ScoreControl is null) return;
        FieldControl.Visibility = TypeInput.SelectedItem is "hash" or "stream" ? Visibility.Visible : Visibility.Collapsed;
        ScoreControl.Visibility = TypeInput.SelectedItem is "zset" ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnCreate(object sender, RoutedEventArgs args)
    {
        if (KeyInput.Text.Length == 0) { KeyField.Error = Text.Get("RedisDraftInvalid"); return; }
        string type = (string)TypeInput.SelectedItem;
        if (type == "zset" && (!double.TryParse(ScoreInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double score) || !double.IsFinite(score)))
        { ScoreControl.Error = Text.Get("RedisDraftInvalid"); return; }
        Request = new(KeyInput.Text, type, FieldInput.Text, ValueInput.Text, ScoreInput.Text); DialogResult = true;
    }
}
