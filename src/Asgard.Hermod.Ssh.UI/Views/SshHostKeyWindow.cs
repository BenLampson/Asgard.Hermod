using System.Runtime.InteropServices;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>完整展示公开指纹；变更身份必须先勾选可信渠道核验。</summary>
public sealed class SshHostKeyWindow : HermodDialog
{
    /// <summary>首次和身份变化使用不同文案，确认前不修改设备信任记录。</summary>
    public SshHostKeyWindow(SshHostKeyChallenge challenge)
    {
        Title = Text.Get("SshHostIdentityTitle"); Width = 620; Height = challenge.HasChanged ? 550 : 440;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = Text.Get(challenge.HasChanged ? "SshHostIdentityChanged" : "SshHostIdentityFirst"),
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20)
        });
        Add(Text.Get("SshHostRole"), Text.Get(challenge.IsJumpHost ? "SshJumpHost" : "SshHost"));
        Add(Text.Get("SshHost"), challenge.Presented.Host + ":" + challenge.Presented.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(Text.Get("SshAlgorithm"), challenge.Presented.Algorithm);
        if (challenge.Previous is { } previous) Add(Text.Get("SshPreviousFingerprint"), previous.Fingerprint);
        Add(Text.Get("SshFingerprint"), challenge.Presented.Fingerprint);
        var copy = new Button { Content = Text.Get("SshCopyFingerprint"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 14) };
        copy.Click += (_, _) => { try { Clipboard.SetText(challenge.Presented.Fingerprint); } catch (ExternalException) { } };
        panel.Children.Add(copy);
        var verified = new CheckBox { Content = new TextBlock { Text = Text.Get("SshVerifiedCheckbox"), TextWrapping = TextWrapping.Wrap } };
        if (challenge.HasChanged) panel.Children.Add(verified);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var stop = new Button { Content = Text.Get("SshStopConnection"), IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
        stop.Click += (_, _) => DialogResult = false;
        var accept = new Button { Content = Text.Get(challenge.HasChanged ? "SshUpdateTrust" : "SshTrustContinue"), IsEnabled = !challenge.HasChanged };
        accept.SetResourceReference(StyleProperty, challenge.HasChanged ? "DangerButton" : "PrimaryButton");
        verified.Checked += (_, _) => accept.IsEnabled = true;
        verified.Unchecked += (_, _) => accept.IsEnabled = false;
        accept.Click += (_, _) => DialogResult = true;
        actions.Children.Add(stop); actions.Children.Add(accept);
        FooterContent = new DialogActionBar { Height = 56, Padding = new Thickness(20, 0, 20, 0), Content = actions };
        Loaded += (_, _) => stop.Focus();

        void Add(string label, string value)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
            var text = new TextBox { Text = value, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 36, MaxHeight = 64 };
            text.SetResourceReference(FontFamilyProperty, "MonoFont");
            System.Windows.Automation.AutomationProperties.SetName(text, label);
            panel.Children.Add(text);
        }
    }
}
