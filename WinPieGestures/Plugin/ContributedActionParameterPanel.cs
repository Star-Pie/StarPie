using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>为认领动作渲染未被旧手写面板承载的声明字段；不识别任何插件私有键。</summary>
public sealed class ContributedActionParameterPanel : StackPanel
{
    private readonly PluginParameterForm _form;
    private ActionItem? _item;
    private Func<ActionItem?>? _writeSource;
    private SubSlotViewModel? _viewModel;
    private bool _loaded;
    private bool _building;
    private bool _includePluginActions;
    public event EventHandler? ValuesChanged;

    public static readonly DependencyProperty HasFieldsProperty = DependencyProperty.Register(
        nameof(HasFields), typeof(bool), typeof(ContributedActionParameterPanel), new PropertyMetadata(false));
    public bool HasFields => (bool)GetValue(HasFieldsProperty);
    public static readonly DependencyProperty UsesLegacyStandardUserProperty = DependencyProperty.Register(
        nameof(UsesLegacyStandardUser), typeof(bool), typeof(ContributedActionParameterPanel), new PropertyMetadata(true));
    public bool UsesLegacyStandardUser => (bool)GetValue(UsesLegacyStandardUserProperty);

    public ContributedActionParameterPanel()
    {
        _form = new PluginParameterForm(this, GetTarget, OnValuesChanged);
        Loaded += (_, _) => { _loaded = true; AttachViewModel(); Rebuild(); };
        Unloaded += (_, _) => { _loaded = false; DetachViewModel(); _form.Reset(); };
        DataContextChanged += (_, _) => { if (_loaded) { AttachViewModel(); Rebuild(); } };
    }

    private IPluginParameterTarget? GetTarget()
    {
        if (_item == null) return null;
        if (_includePluginActions && _item.Type == PluginActionBinding.TypeName)
            return new ActionItemParameterTarget(_item);
        return new ContributedActionParameterTarget(_item, _writeSource);
    }

    private void OnValuesChanged()
    {
        if (_writeSource != null) _item = _writeSource();
        _form.ShowIssues(_form.Validate());
        ValuesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAction(ActionItem? item, Func<ActionItem?>? writeSource = null)
    {
        _item = item;
        _writeSource = writeSource;
        Rebuild();
    }

    private void AttachViewModel()
    {
        DetachViewModel();
        _includePluginActions = false;
        if (DataContext is SubSlotViewModel vm)
        {
            _includePluginActions = true;
            _viewModel = vm;
            _item = vm.Action;
            vm.PropertyChanged += OnViewModelChanged;
        }
    }

    private void DetachViewModel()
    {
        if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = null;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SubSlotViewModel.Type) or nameof(SubSlotViewModel.Action) or nameof(SubSlotViewModel.SelectedPluginActionFullId))
        {
            _item = _viewModel?.Action;
            Rebuild();
        }
    }

    private void Rebuild()
    {
        if (_building) return;
        _building = true;
        try
        {
            _form.Reset();
            SetValue(HasFieldsProperty, false);
            SetValue(UsesLegacyStandardUserProperty, true);
            Visibility = Visibility.Collapsed;
            if (_item == null) return;
            if (_includePluginActions && _item.Type == PluginActionBinding.TypeName && _item.PluginActionRef != null &&
                PluginHost.TryGetAction(_item.PluginActionRef.FullId, out PluginActionRegistration pluginRegistration))
            {
                BuildRegisteredFields(pluginRegistration, true);
                return;
            }
            if (PluginHost.TryGetClaimedEditorRegistration(_item.Type, out PluginActionRegistration registration))
                BuildRegisteredFields(registration);
        }
        finally { _building = false; }
    }

    internal void BuildRegisteredFields(PluginActionRegistration registration, bool includeLegacyFields = false)
    {
        var fields = includeLegacyFields ? registration.Parameters : registration.Parameters
            .Where(field => !HostActionFields.All.Contains(field.Key, StringComparer.OrdinalIgnoreCase)).ToArray();
        _form.Build(fields, registration.PluginId);
        SetValue(UsesLegacyStandardUserProperty, registration.Parameters.Any(field =>
            string.Equals(field.Key, HostActionFields.RunAsStandardUser, StringComparison.OrdinalIgnoreCase)));
        bool hasFields = !_form.IsEmpty;
        SetValue(HasFieldsProperty, hasFields);
        Visibility = hasFields ? Visibility.Visible : Visibility.Collapsed;
    }
}
