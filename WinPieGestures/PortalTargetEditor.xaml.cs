using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Pen = System.Windows.Media.Pen;

namespace WinPieGestures;

internal sealed record PortalEditorContext(AppConfig Config, PortalLayerAddress Source, Action<PortalLayerAddress> Jump,
    Action<WheelPortalTarget> Preview);

public partial class PortalTargetEditor : UserControl
{
    private sealed record Choice(string Key, string Caption);
    internal sealed record Destination(PortalLayerAddress Address, string Caption);
    private PortalEditorContext? _context;
    private ActionItem? _item;
    private Action<WheelPortalTarget>? _commit;
    private string? _source;
    private bool _filling;
    private bool _deferIdentity;

    public PortalTargetEditor() => InitializeComponent();

    internal static void ApplyTarget(ActionItem action, WheelPortalTarget target)
    {
        action.PortalTarget = target;
        if (target.SyncTargetName && !string.IsNullOrWhiteSpace(target.TargetLayerNameSnapshot))
            action.Name = target.TargetLayerNameSnapshot;
    }

    internal void Bind(PortalEditorContext? context, ActionItem? item, Action<WheelPortalTarget>? commit, bool deferIdentity = false)
    {
        _context = item?.IsWheelPortal == true ? context : null;
        _item = item;
        _commit = commit;
        _source = null;
        _deferIdentity = deferIdentity;
        Visibility = _context == null ? Visibility.Collapsed : Visibility.Visible;
        if (_context == null)
        {
            Thumbnail.SetTarget(null, null);
            return;
        }
        Refresh();
    }

    internal static IEnumerable<Destination> DestinationsFor(IReadOnlyList<WheelProfile> profiles, PortalLayerAddress source, string group)
    {
        foreach (var profile in profiles)
        {
            bool same = ReferenceEquals(profile, source.Profile);
            if (group == "same" ? !same : same || (group == "parallel") == profile.UseApplicationContext) continue;
            foreach (var layer in profile.Layers)
            {
                var address = new PortalLayerAddress(profile, layer);
                if (address == source) continue;
                yield return new(address, same ? layer.DisplayName : profile.FriendlyName + " · " + layer.DisplayName);
            }
        }
    }

    private void Refresh()
    {
        if (_context is not { } context) return;
        var target = _item?.PortalTarget;
        PortalLayerAddress? selected = target == null ? null : PortalReferenceAnalyzer.Resolve(context.Config.Profiles, context.Source.Profile, target);
        if (selected == context.Source) selected = null;
        _source ??= target?.SameProfile != false ? "same" : selected?.Profile.UseApplicationContext == false ? "parallel" : "apps";
        _filling = true;
        try
        {
            Title.Text = I18n.T("PortalTarget");
            AutomationProperties.SetName(Sources, I18n.T("PortalSource"));
            AutomationProperties.SetName(Destinations, Title.Text);
            AutomationProperties.SetName(Anchors, I18n.T("PortalAnchor"));
            Sources.ItemsSource = new Choice[] { new("same", I18n.T("PortalSourceSame")), new("parallel", I18n.T("PortalSourceParallel")), new("apps", I18n.T("PortalSourceApp")) };
            Sources.SelectedValue = _source;
            var choices = DestinationsFor(context.Config.Profiles, context.Source, _source).ToArray();
            Destinations.ItemsSource = choices;
            Destinations.SelectedItem = choices.FirstOrDefault(option => option.Address == selected);
            Destinations.IsEnabled = choices.Length > 0;
            Anchors.ItemsSource = new Choice[] { new("SectorCenter", I18n.T("PortalAnchorSector")), new("OuterOffset", I18n.T("PortalAnchorOuter")) };
            Anchors.SelectedValue = target?.IsOuterOffset == true ? "OuterOffset" : "SectorCenter";
            Anchors.IsEnabled = selected.HasValue;
            SyncName.Content = I18n.T("PortalSyncName");
            SyncName.IsChecked = target?.SyncTargetName ?? true;
            SyncName.IsEnabled = selected.HasValue;
            Jump.Content = I18n.T("PortalJump");
            Ghost.Content = I18n.T("PortalPreview");
            Jump.IsEnabled = Ghost.IsEnabled = selected.HasValue;
            Thumbnail.SetTarget(context.Config, selected);
            Warning.Text = "";
            if (selected is { } address)
            {
                var analyzer = new PortalReferenceAnalyzer(context.Config.Profiles);
                if (analyzer.FindCyclePath(context.Source, address) is { } cycle)
                    Warning.Text = I18n.TF("PortalCycle", string.Join(" › ", cycle.Select(p => p.Profile.FriendlyName + " · " + p.Layer.DisplayName)));
            }
            else if (target != null)
            {
                string previous = string.Join(" · ", new[] { target.TargetProfileNameSnapshot, target.TargetLayerNameSnapshot }
                    .Where(name => !string.IsNullOrWhiteSpace(name)));
                Warning.Text = I18n.T("PortalBroken") + (previous.Length == 0 ? "" : " · " + previous);
            }
            else
                Warning.Text = I18n.T(choices.Length == 0 ? "PortalNoTargets" : "PortalChooseTarget");
            Warning.Visibility = Warning.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _filling = false; }
    }

    private void SourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || Sources.SelectedValue is not string source) return;
        _source = source;
        Refresh();
    }

    private void DestinationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _context is not { } context || Destinations.SelectedItem is not Destination option ||
            option.Address == context.Source || !context.Config.Profiles.Contains(option.Address.Profile)) return;
        var target = _item?.PortalTarget?.Clone() ?? new WheelPortalTarget();
        target.SameProfile = ReferenceEquals(option.Address.Profile, context.Source.Profile);
        target.TargetProfileKey = target.SameProfile ? "" : option.Address.Profile.ProcessName;
        target.TargetLayerIndex = option.Address.Profile.Layers.IndexOf(option.Address.Layer);
        if (target.TargetLayerIndex < 0) return;
        target.TargetLayerId = _deferIdentity ? option.Address.Layer.LayerId : option.Address.Layer.EnsureLayerId();
        target.TargetProfileNameSnapshot = option.Address.Profile.FriendlyName;
        target.TargetLayerNameSnapshot = option.Address.Layer.DisplayName;
        Commit(target);
    }

    private void AnchorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _item?.PortalTarget is not { } current || Anchors.SelectedValue is not string anchor) return;
        var target = current.Clone();
        target.AnchorMode = anchor;
        Commit(target);
    }

    private void SyncChanged(object sender, RoutedEventArgs e)
    {
        if (_filling || _item?.PortalTarget is not { } current) return;
        var target = current.Clone();
        target.SyncTargetName = SyncName.IsChecked == true;
        Commit(target);
    }

    private void Commit(WheelPortalTarget target)
    {
        _commit?.Invoke(target);
        Refresh();
    }

    private void JumpClick(object sender, RoutedEventArgs e)
    {
        if (_context is { } context && _item?.PortalTarget is { } target &&
            PortalReferenceAnalyzer.Resolve(context.Config.Profiles, context.Source.Profile, target) is { } address && address != context.Source)
            context.Jump(address);
    }

    private void GhostClick(object sender, RoutedEventArgs e)
    {
        if (_item?.PortalTarget is { } target) _context?.Preview(target);
    }
}

public sealed class PortalWheelPreview : FrameworkElement
{
    private AppConfig? _config;
    private PortalLayerAddress? _target;

    internal void SetTarget(AppConfig? config, PortalLayerAddress? target)
    {
        _config = config;
        _target = target;
        Visibility = target.HasValue ? Visibility.Visible : Visibility.Collapsed;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_config is not { } config || _target is not { } target || ActualWidth <= 0 || ActualHeight <= 0) return;
        var layer = target.Layer;
        var ink = TryFindResource("AccentPrimaryBrush") as Brush ?? System.Windows.Media.Brushes.SteelBlue;
        var fill = TryFindResource("CardBackgroundBrush") as Brush ?? System.Windows.Media.Brushes.Transparent;
        int sectors = Math.Clamp(layer.SectorCount, 4, 12);
        int depth = config.EnableMultiTier ? layer.EffectiveMaxMenuDepth : 1;
        double radius = Math.Max(1, config.WheelRadius);
        double secondary = LegacyRingGeometry.SecondaryExtent(config, radius, config.InnerRadius);
        FixedRingGeometry? geometry = null;
        var menu = layer.ActiveFixedRings;
        if (menu != null && depth >= 3)
        {
            bool honeycomb = config.SubmenuStyle == "Fan";
            double start = FixedRingGeometry.GetThirdInnerRadius(config, honeycomb ? 0 : secondary, radius / 72, radius);
            geometry = new FixedRingGeometry(sectors, radius, start, radius / 72, honeycomb, config);
        }
        double extent = geometry != null ? geometry.OuterForCount(depth, menu!.GetCount(depth)) :
            depth >= 2 && layer.Actions.Any(action => action.InlineSubActions is { Count: > 0 }) ? Math.Max(radius, secondary) : radius;
        double scale = Math.Max(.001, (Math.Min(ActualWidth, ActualHeight) - 8) / (2 * extent));
        var translate = new TranslateTransform(ActualWidth / 2, ActualHeight / 2);
        translate.Freeze();
        var zoom = new ScaleTransform(scale, scale);
        zoom.Freeze();
        dc.PushTransform(translate);
        dc.PushTransform(zoom);
        var outline = new Pen(ink, 1.25 / scale);
        if (outline.CanFreeze) outline.Freeze();
        for (int sector = 0; sector < sectors; sector++)
        {
            var primary = geometry?.Primary(config, sector) ?? LegacyRingGeometry.Primary(config, sectors, sector, radius, config.InnerRadius);
            dc.DrawGeometry(fill, outline, primary.Shape);
            var children = sector < layer.Actions.Count ? layer.Actions[sector].InlineSubActions : null;
            if (depth < 2 || children == null) continue;
            int count = DeepMenuFrame.VisibleSlotCount(children.Count, config.SubmenuStyle == "Fan");
            for (int i = 0; i < count; i++)
            {
                var cell = geometry?.Secondary(config, sector, i, count) ??
                    LegacyRingGeometry.Secondary(config, sectors, sector, i, count, radius, config.InnerRadius);
                dc.DrawGeometry(fill, outline, cell.Shape);
            }
        }
        if (geometry != null)
            for (int level = 3; level <= depth; level++)
                for (int i = 0; i < menu!.GetCount(level); i++)
                    if (WheelProfile.IsActionConfigured(menu.GetSlot(level, i)?.Action))
                        dc.DrawGeometry(fill, outline, geometry.GetShape(level, i, menu.GetCount(level)));
        double core = geometry?.CoreRadius(config.CoreRadius) ?? config.CoreRadius;
        if (config.ShowCoreCircle && core > 0) dc.DrawEllipse(fill, outline, new Point(), core, core);
        dc.Pop();
        dc.Pop();
    }
}
