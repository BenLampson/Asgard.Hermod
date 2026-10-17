using System.Collections.Specialized;

namespace Asgard.Hermod.UI.Tests;

/// <summary>约束所有连接表单共用的集合身份、增量更新及订阅生命周期。</summary>
public sealed class InstanceConfigurationEditorTests
{
    /// <summary>输入、校验与步骤状态不会重发集合变更，绑定方也不能自行清空公共列表。</summary>
    [Fact]
    public void EditingAndValidation_KeepBothCollectionsStable()
    {
        using var wizard = CreateWizard();
        var editor = wizard.ConfigurationEditor;
        var fields = editor.Fields;
        var visible = editor.VisibleFields;
        var changes = new List<NotifyCollectionChangedEventArgs>();
        ((INotifyCollectionChanged)fields).CollectionChanged += (_, args) => changes.Add(args);
        ((INotifyCollectionChanged)visible).CollectionChanged += (_, args) => changes.Add(args);
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        wizard.Name = "connection-123";
        foreach (var field in wizard.Fields) field.Value = "test123";

        Assert.Same(fields, editor.Fields);
        Assert.Same(visible, editor.VisibleFields);
        Assert.Equal(wizard.Fields, fields);
        Assert.Empty(changes);
        Assert.Throws<NotSupportedException>(() => ((IList<InstanceConfigurationFieldViewModel>)fields).Clear());
    }

    /// <summary>分区改变只增删差异成员，仍适用的字段状态与集合身份保持不变。</summary>
    [Fact]
    public void FilterChanges_UpdateMembersWithoutResettingCollection()
    {
        using var wizard = CreateWizard();
        using var editor = new FilteredEditor(wizard);
        var fields = editor.Fields;
        var key = Assert.Single(fields, item => !item.IsSecret);
        var secret = Assert.Single(fields, item => item.IsSecret);
        key.Value = "retained123";
        var actions = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)fields).CollectionChanged += (_, args) => actions.Add(args.Action);

        editor.IncludeSecrets = false;
        Assert.Same(fields, editor.Fields);
        Assert.Same(key, Assert.Single(editor.Fields));
        editor.IncludeSecrets = true;
        Assert.Same(secret, Assert.Single(editor.Fields, item => item.IsSecret));
        Assert.Equal("retained123", key.Value);
        Assert.Equal([NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add], actions);
    }

    /// <summary>切换服务商释放旧字段，重复挂载不会重复订阅，离开期间的源变化会在回来时同步。</summary>
    [Fact]
    public void ProviderChangesAndReattachment_ReplaceSourcesAndReleaseSubscriptions()
    {
        using var wizard = CreateWizard();
        using var editor = new FilteredEditor(wizard);
        var fields = editor.Fields;
        var old = wizard.Fields[0];
        editor.Attach();
        old.Value = "first123";
        Assert.Equal(1, editor.ValueChanges);

        wizard.SelectedProvider = wizard.Providers[1];
        Assert.Same(fields, editor.Fields);
        Assert.Equal(wizard.Fields, editor.Fields);
        Assert.DoesNotContain(old, editor.Fields);
        int afterReplacement = editor.ValueChanges;
        old.Value = "obsolete123";
        Assert.Equal(afterReplacement, editor.ValueChanges);
        editor.Dispose();
        wizard.Fields[0].Value = "detached123";
        wizard.SelectedProvider = wizard.Providers[0];
        Assert.Equal(afterReplacement, editor.ValueChanges);

        editor.Attach();
        editor.Attach();
        Assert.Same(fields, editor.Fields);
        Assert.Equal(wizard.Fields, editor.Fields);
        wizard.Fields[0].Value = "reattached123";
        Assert.Equal(afterReplacement + 1, editor.ValueChanges);
    }

    /// <summary>固定测试元数据不依赖系统显示语言，也不建立外部连接。</summary>
    private static NewInstanceViewModel CreateWizard()
    {
        var wizard = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(
            new NewInstanceWizardTestProvider("first"), new NewInstanceWizardTestProvider("second")),
            new NewInstanceWizardTestConfigurationService());
        wizard.SelectedType = Assert.Single(wizard.Types);
        wizard.SelectedProvider = wizard.Providers[0];
        return wizard;
    }

    /// <summary>模拟模块只声明筛选规则和依赖字段值的提示，不编写集合刷新或订阅代码。</summary>
    private sealed class FilteredEditor : InstanceConfigurationEditorViewModel
    {
        public FilteredEditor(NewInstanceViewModel wizard) : base(wizard) => Attach();
        public bool IncludeSecrets { get; set { if (SetProperty(ref field, value)) RefreshFields(); } } = true;
        public int ValueChanges { get; private set; }
        protected override bool IncludesField(InstanceConfigurationFieldViewModel item) => IncludeSecrets || !item.IsSecret;
        protected override void OnFieldValueChanged(InstanceConfigurationFieldViewModel item) => ValueChanges++;
    }
}
