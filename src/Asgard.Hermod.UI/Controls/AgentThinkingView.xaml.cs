using System.Windows.Media.Animation;

namespace Asgard.Hermod.UI.Controls;

/// <summary>真实思考的可访问披露控件；仅文字扫光，不移动文字或背景。</summary>
public partial class AgentThinkingView : UserControl
{
    private readonly DispatcherTimer _timer;
    private TranslateTransform? _sweep;
    private ScrollViewer? _viewport;
    /// <summary>由消息持有的思考与展开状态。</summary>
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(AgentThinkingState), typeof(AgentThinkingView), new PropertyMetadata(null, OnStateChanged));
    /// <summary>绑定独立思考状态。</summary>
    public AgentThinkingState? State { get => (AgentThinkingState?)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    /// <summary>生命周期内订阅状态；卸载后释放定时器与系统事件。</summary>
    public AgentThinkingView()
    {
        InitializeComponent();
        _timer = new(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => State?.RefreshElapsed(), Dispatcher); _timer.Stop();
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= OnSystemSettingChanged;
            if (_viewport is not null) _viewport.ScrollChanged -= OnViewportScrolled;
            _viewport = null; StopAnimation();
        };
        IsVisibleChanged += (_, _) => RefreshAnimation();
    }
    private static void OnStateChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var view = (AgentThinkingView)owner;
        if (args.OldValue is AgentThinkingState oldState) PropertyChangedEventManager.RemoveHandler(oldState, view.OnStatePropertyChanged, "");
        if (args.NewValue is AgentThinkingState newState) PropertyChangedEventManager.AddHandler(newState, view.OnStatePropertyChanged, "");
        view.Refresh();
    }
    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AgentThinkingState.Content)) RefreshText();
        if (args.PropertyName == nameof(AgentThinkingState.IsActive)) RefreshAnimation();
        if (args.PropertyName == nameof(AgentThinkingState.IsExpanded) && State?.IsExpanded == true && IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation)
            ThinkingBody.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }
    private void Refresh() { RefreshText(); RefreshAnimation(); }
    private void RefreshText()
    {
        if (ReasoningText is null || ReasoningText.Text == State?.Content) return;
        int start = ReasoningText.SelectionStart, length = ReasoningText.SelectionLength;
        double offset = ReasoningText.VerticalOffset;
        bool follow = ReasoningText.ExtentHeight - ReasoningText.ViewportHeight - offset <= 2 && length == 0;
        ReasoningText.Text = State?.Content ?? "";
        ReasoningText.Select(Math.Min(start, ReasoningText.Text.Length), Math.Min(length, ReasoningText.Text.Length - Math.Min(start, ReasoningText.Text.Length)));
        if (follow) ReasoningText.ScrollToEnd(); else ReasoningText.ScrollToVerticalOffset(offset);
    }
    private void OnSystemSettingChanged(object? sender, PropertyChangedEventArgs args) => RefreshAnimation();
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        SystemParameters.StaticPropertyChanged += OnSystemSettingChanged;
        for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer viewport) { _viewport = viewport; viewport.ScrollChanged += OnViewportScrolled; break; }
        Refresh();
    }
    private void OnViewportScrolled(object sender, ScrollChangedEventArgs args) => RefreshAnimation();
    private bool IsInViewport() => _viewport is null || TransformToAncestor(_viewport).TransformBounds(new Rect(RenderSize)).IntersectsWith(new Rect(_viewport.RenderSize));
    private void RefreshAnimation()
    {
        if (_timer is null) return;
        bool active = IsLoaded && IsVisible && State?.IsActive == true && IsInViewport();
        if (active) _timer.Start(); else _timer.Stop();
        if (!active || !SystemParameters.ClientAreaAnimation) { StopSweep(); return; }
        if (_sweep is not null) return;
        var muted = (TryFindResource("TextMutedBrush") as SolidColorBrush)?.Color ?? Colors.Gray;
        _sweep = new TranslateTransform(-1, 0);
        var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0), RelativeTransform = _sweep };
        brush.GradientStops.Add(new(muted, 0.32)); brush.GradientStops.Add(new(Color.FromRgb(238, 242, 247), 0.5)); brush.GradientStops.Add(new(muted, 0.68));
        ThinkingLabel.Foreground = brush;
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1800), RepeatBehavior = RepeatBehavior.Forever };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1400))));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1800))));
        _sweep.BeginAnimation(TranslateTransform.XProperty, animation);
    }
    private void StopSweep()
    {
        _sweep?.BeginAnimation(TranslateTransform.XProperty, null); _sweep = null;
        ThinkingLabel?.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
    }
    private void StopAnimation() { _timer.Stop(); StopSweep(); ThinkingBody.BeginAnimation(OpacityProperty, null); }
    private void OnToggleKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && State is { } state) { state.IsExpanded = !state.IsExpanded; args.Handled = true; }
    }
}
