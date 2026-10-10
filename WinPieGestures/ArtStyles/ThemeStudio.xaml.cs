using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace WinPieGestures.ArtStyles;

public sealed class ArtStyleDraftEventArgs(ArtStyleProfile? profile) : EventArgs
{
    public ArtStyleProfile? Profile { get; } = profile?.Copy();
}

public partial class ThemeStudio : UserControl
{
    private bool refreshing = true;
    private bool subscribed;
    private string selectedId = ArtStyleCatalog.DefaultId;
    private ArtStyleProfile? draft;
    private ArtStyleProfile? originalDraft;
    public event EventHandler<ArtStyleDraftEventArgs>? DraftPreviewChanged;
    private AppConfig Config => ConfigManager.CurrentConfig;

    public ThemeStudio()
    {
        InitializeComponent();
        FontBox.AddHandler(TextBoxBase.TextChangedEvent,new TextChangedEventHandler(Font_Changed));
        Loaded += (_,_) => Subscribe();
        Unloaded += (_,_) => Unsubscribe();
        if (!string.IsNullOrEmpty(Config.SelectedArtStyleId)) selectedId = Config.SelectedArtStyleId;
        Refresh();
    }

    private void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;
        WeakEventManager<ArtStyleService,EventArgs>.AddHandler(ArtStyleService.Instance,nameof(ArtStyleService.Changed),Service_Changed);
        I18n.LanguageChanged += Language_Changed;
        Refresh();
    }
    private void Unsubscribe()
    {
        if (!subscribed) return;
        subscribed = false;
        WeakEventManager<ArtStyleService,EventArgs>.RemoveHandler(ArtStyleService.Instance,nameof(ArtStyleService.Changed),Service_Changed);
        I18n.LanguageChanged -= Language_Changed;
    }
    private void Language_Changed()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Language_Changed); return; }
        Refresh();
    }
    private void Service_Changed(object? sender, EventArgs e)
    {
        CancelDraft();
        Refresh();
    }

    public void Refresh()
    {
        refreshing = true;
        try
        {
            AppThemeManager.ApplyTheme(this,Config.AppTheme);
            TitleText.Text = ArtStyleText.Get("Title");
            DescriptionText.Text = ArtStyleText.Get("Description");
            FollowBox.Content = ArtStyleText.Get("Follow");
            FollowBox.IsChecked = Config.ArtStyleFollowsWheel;
            FollowHint.Text = ArtStyleText.Get("FollowHint");
            EditorTitle.Text = ArtStyleText.Get("Editor"); DraftHint.Text = ArtStyleText.Get("DraftHint");
            NameLabel.Text = ArtStyleText.Get("Name"); AdvancedPanel.Header = ArtStyleText.Get("Advanced");
            FontLabel.Text = ArtStyleText.Get("FontFamily"); DarkBox.Content = ArtStyleText.Get("IsDark");
            DecorationLabel.Text = ArtStyleText.Get("Decoration");
            foreach (var pair in new Dictionary<Button,string> { [ApplyButton]="Apply", [NewButton]="New", [RenameButton]="Rename", [DeleteButton]="Delete", [ImportButton]="Import", [ExportButton]="Export", [SaveDraftButton]="Save", [CancelButton]="Cancel", [ResetButton]="Reset" }) pair.Key.Content = ArtStyleText.Get(pair.Value);
            if (ArtStyleCatalog.Find(Config,selectedId) is not { } selected || ArtStyleResolver.Validate(selected).Count>0) selectedId = ArtStyleCatalog.DefaultId;
            RefreshCards();
            if (draft != null) BuildEditorFields();
            var active = ArtStyleResolver.Resolve(Config);
            CurrentText.Text = ArtStyleText.Get("Current") + ": " + (active == null ? ArtStyleText.Get("Legacy") : ArtStyleText.Name(active));
            if (!string.IsNullOrEmpty(Config.SelectedArtStyleId) && (ArtStyleCatalog.Find(Config,Config.SelectedArtStyleId) is not { } requested || ArtStyleResolver.Validate(requested).Count > 0)) StatusText.Text = ArtStyleText.Get("Missing");
        }
        finally { refreshing = false; }
        UpdatePreview();
    }

    public void ReloadFromConfig()
    {
        CancelDraft();
        if (!string.IsNullOrEmpty(Config.SelectedArtStyleId)) selectedId=Config.SelectedArtStyleId;
        Refresh();
    }

    private sealed class ThemeCard
    {
        public required ArtStyleProfile Profile { get; init; }
        public string Name => ArtStyleText.Name(Profile);
        public string Description => ArtStyleText.Get(ArtStyleCatalog.IsBuiltIn(Profile.Id) ? Profile.Id+".Desc" : "Custom");
        public required string ActiveLabel { get; init; }
        public required Brush Surface { get; init; }
        public required Brush Ink { get; init; }
        public required Brush Frame { get; init; }
        public required Brush Accent { get; init; }
        public required Brush Swatch { get; init; }
        public required Thickness FrameThickness { get; init; }
        public required FrameworkElement Sample { get; init; }
    }
    private void RefreshCards()
    {
        ThemeCards.ItemsSource = ArtStyleCatalog.GetAll(Config).Where(p=>ArtStyleResolver.Validate(p).Count==0).Select(p=>new ThemeCard
        {
            Profile=p, ActiveLabel=p.Id==Config.SelectedArtStyleId ? ArtStyleText.Get("Active") : ArtStyleCatalog.IsBuiltIn(p.Id) ? p.Name.ToUpperInvariant() : ArtStyleText.Get("Custom"),
            Surface=ArtStyleResources.Brush(p.Colors.Surface), Ink=ArtStyleResources.Brush(p.Colors.Text),
            Frame=ArtStyleResources.Brush(p.Id==selectedId ? p.Colors.Accent : p.Colors.Border), FrameThickness=new Thickness(p.Id==selectedId?2:1),
            Accent=ArtStyleResources.Brush(p.Colors.Accent), Swatch=ArtStyleResources.Brush(p.Colors.Sidebar), Sample=ArtStyleSamples.Wheel(p,54)
        }).ToArray();
        bool user = !ArtStyleCatalog.IsBuiltIn(selectedId);
        EditButton.Content = ArtStyleText.Get(user?"Edit":"CopyEdit");
        RenameButton.IsEnabled = DeleteButton.IsEnabled = user && draft==null;
        ApplyButton.IsEnabled = EditButton.IsEnabled = NewButton.IsEnabled = ImportButton.IsEnabled = ExportButton.IsEnabled = draft==null;
        FollowBox.IsEnabled = draft==null;
    }

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (draft != null) return;
        selectedId = (string)((Button)sender).Tag;
        RefreshCards(); UpdatePreview(); StatusText.Text="";
    }
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ArtStyleService.Select(Config,selectedId,FollowBox.IsChecked==true);
        Persist("Applied");
    }
    private void Follow_Changed(object sender, RoutedEventArgs e)
    {
        if (refreshing || draft != null) return;
        Config.ArtStyleFollowsWheel = FollowBox.IsChecked==true;
        Persist("Applied");
    }
    private void Edit_Click(object sender, RoutedEventArgs e) => BeginEdit(ArtStyleCatalog.IsBuiltIn(selectedId));
    private void New_Click(object sender, RoutedEventArgs e)
    {
        selectedId=ArtStyleCatalog.DefaultId; BeginEdit(true); DraftNameBox.Text=ArtStyleText.Get("NewName");
    }
    private void BeginEdit(bool copy)
    {
        draft = (ArtStyleCatalog.Find(Config,selectedId) ?? ArtStyleCatalog.Find(Config,ArtStyleCatalog.DefaultId))!;
        if (copy || ArtStyleCatalog.IsBuiltIn(draft.Id))
        {
            string name=ArtStyleText.Name(draft)+ArtStyleText.Get("CopySuffix");
            draft.Id="user-"+Guid.NewGuid().ToString("N"); draft.Name=name;
        }
        originalDraft=draft.Copy();
        EditorPanel.Visibility=Visibility.Visible;
        refreshing=true;
        try { BuildEditorFields(); RefreshCards(); } finally { refreshing=false; }
        UpdatePreview();
    }
    private void BuildEditorFields()
    {
        if (draft==null) return;
        DraftNameBox.Text=draft.Name;
        DarkBox.IsChecked=draft.IsDark;
        FontBox.ItemsSource=new[] { "Microsoft YaHei UI, Segoe UI", "Cascadia Mono, Consolas, Microsoft YaHei UI", "Georgia, Microsoft YaHei UI", "Microsoft JhengHei UI, Segoe UI", "Yu Gothic UI, Microsoft YaHei UI" };
        FontBox.Text=draft.FontFamily;
        DecorationBox.ItemsSource=new[] { "Paper","Orbit","Soft","Glass","Pixel" }.Select(x=>new ComboBoxItem { Tag=x, Content=ArtStyleText.Get(x switch { "Orbit"=>"obsidian","Soft"=>"grove","Glass"=>"glacier","Pixel"=>"pixel",_=>"paper" }) }).ToArray();
        DecorationBox.SelectedIndex=Array.IndexOf(new[] { "Paper","Orbit","Soft","Glass","Pixel" },draft.Decoration);
        PaletteFields.Children.Clear();
        foreach (var property in typeof(ArtStylePalette).GetProperties()) AddColorField(property);
        MetricFields.Children.Clear();
        AddMetric(nameof(ArtStyleProfile.CornerRadius),0,24);
        AddMetric(nameof(ArtStyleProfile.StrokeWidth),.5,3);
        AddMetric(nameof(ArtStyleProfile.ShadowBlur),0,30);
        AddMetric(nameof(ArtStyleProfile.ShadowDepth),0,6);
        AddMetric(nameof(ArtStyleProfile.ShadowOpacity),0,.5);
        AddMetric(nameof(ArtStyleProfile.WheelOpacity),.6,1);
        AddMetric(nameof(ArtStyleProfile.DecorationStrength),0,1);
    }
    private void AddColorField(PropertyInfo property)
    {
        var row=new Grid { Margin=new Thickness(0,0,0,6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        var label=new TextBlock { Text=ArtStyleText.Get("Color."+property.Name), Style=(Style)FindResource("StudioLabel"), VerticalAlignment=VerticalAlignment.Center };
        var input=new TextBox { Text=(string)property.GetValue(draft!.Colors)!, Style=(Style)FindResource("StudioInput"), MaxLength=9, MinWidth=72 };
        input.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty,"ArtColor"+property.Name);
        var picker=new Button { Width=29, Height=29, Padding=new Thickness(0), Margin=new Thickness(6,0,0,0), Content="●", Style=(Style)FindResource("StudioButton"), ToolTip=label.Text, Foreground=ColorSwatch(input.Text) };
        input.TextChanged+=(_,_)=>
        {
            if (refreshing || draft==null) return;
            property.SetValue(draft.Colors,input.Text.Trim());
            picker.Foreground=ColorSwatch(input.Text.Trim());
            UpdatePreview();
        };
        picker.Click+=(_,_)=>
        {
            string hex=(string)property.GetValue(draft!.Colors)!;
            var colorPicker=new ColorPickerWindow(ArtStyleResolver.Validate(draft).Count==0?hex:"#2563EB") { Owner=Window.GetWindow(this) };
            if (colorPicker.ShowDialog()==true) input.Text=colorPicker.SelectedHexColor;
        };
        Grid.SetColumn(input,1); Grid.SetColumn(picker,2); row.Children.Add(label); row.Children.Add(input); row.Children.Add(picker); PaletteFields.Children.Add(row);
    }
    private static Brush ColorSwatch(string hex) => ArtStyleResolver.IsValidColor(hex) ? ArtStyleResources.Brush(hex) : Brushes.Transparent;
    private void AddMetric(string name,double min,double max)
    {
        var property=typeof(ArtStyleProfile).GetProperty(name)!;
        double value=(double)property.GetValue(draft)!;
        var row=new Grid { Margin=new Thickness(0,0,0,9) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(120) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(42) });
        var label=new TextBlock { Text=ArtStyleText.Get(name), Style=(Style)FindResource("StudioLabel"), VerticalAlignment=VerticalAlignment.Center };
        var slider=new Slider { Minimum=min,Maximum=max,Value=value,SmallChange=(max-min)/100,VerticalAlignment=VerticalAlignment.Center };
        var number=new TextBlock { Text=value.ToString("0.##",CultureInfo.CurrentCulture), Style=(Style)FindResource("StudioLabel"), HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center };
        slider.ValueChanged+=(_,e)=> { if (refreshing || draft==null) return; property.SetValue(draft,e.NewValue); number.Text=e.NewValue.ToString("0.##",CultureInfo.CurrentCulture); UpdatePreview(); };
        Grid.SetColumn(slider,1); Grid.SetColumn(number,2); row.Children.Add(label); row.Children.Add(slider); row.Children.Add(number); MetricFields.Children.Add(row);
    }
    private void Name_Changed(object sender,TextChangedEventArgs e) { if (!refreshing && draft!=null) { draft.Name=DraftNameBox.Text; UpdatePreview(); } }
    private void Font_Changed(object sender,TextChangedEventArgs e) { if (!refreshing && draft!=null) { draft.FontFamily=FontBox.Text; UpdatePreview(); } }
    private void Dark_Changed(object sender,RoutedEventArgs e) { if (!refreshing && draft!=null) { draft.IsDark=DarkBox.IsChecked==true; UpdatePreview(); } }
    private void Decoration_Changed(object sender,SelectionChangedEventArgs e) { if (!refreshing && draft!=null && DecorationBox.SelectedItem is ComboBoxItem item) { draft.Decoration=(string)item.Tag; UpdatePreview(); } }

    private void UpdatePreview()
    {
        var profile=draft ?? ArtStyleCatalog.Find(Config,selectedId);
        if (profile==null) return;
        var errors=ArtStyleResolver.Validate(profile);
        SaveDraftButton.IsEnabled=errors.Count==0;
        if (errors.Count>0) { ValidationText.Text=ArtStyleText.Get("Invalid")+" "+string.Join(", ",errors); return; }
        double body=ArtStyleResolver.Contrast(profile.Colors.Text,profile.Colors.Surface);
        double hover=ArtStyleResolver.Contrast(profile.Colors.WheelHighlightText,profile.Colors.WheelHighlight);
        double muted=ArtStyleResolver.Contrast(profile.Colors.Muted,profile.Colors.Surface);
        ValidationText.Text=$"{ArtStyleText.Get("Contrast")}: {body:0.0}:1 / {hover:0.0}:1" + (Math.Min(Math.Min(body,hover),muted)<4.5?"\n"+ArtStyleText.Get("LowContrast"):"");
        WheelSample.Content=ArtStyleSamples.Wheel(profile,150);
        var sample=new Border { Padding=new Thickness(11), CornerRadius=new CornerRadius(profile.CornerRadius), BorderThickness=new Thickness(profile.StrokeWidth), BorderBrush=ArtStyleResources.Brush(profile.Colors.Border), Background=ArtStyleResources.Brush(profile.Colors.Surface), MaxWidth=220 };
        var stack=new StackPanel();
        stack.Children.Add(new TextBlock { Text=ArtStyleText.Get("SampleTitle"),FontSize=16,FontWeight=FontWeights.SemiBold,Foreground=ArtStyleResources.Brush(profile.Colors.Text) });
        stack.Children.Add(new TextBlock { Text=ArtStyleText.Get("SampleHint"),FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,10),Foreground=ArtStyleResources.Brush(profile.Colors.Muted) });
        var button=new Button { Content=ArtStyleText.Get("SampleButton"),Style=(Style)FindResource("StudioButton"),Background=ArtStyleResources.Brush(profile.Colors.Accent),Foreground=ArtStyleResources.Brush(ArtStyleResolver.Contrast("#FFFFFF",profile.Colors.Accent)>=4.5?"#FFFFFF":"#11171B") };
        stack.Children.Add(button); sample.Child=stack;
        ArtStyleResources.ApplyPreview(sample,profile); ControlSample.Content=sample;
        if (draft!=null) DraftPreviewChanged?.Invoke(this,new ArtStyleDraftEventArgs(draft));
    }
    private void CancelDraft()
    {
        draft=null; originalDraft=null; EditorPanel.Visibility=Visibility.Collapsed;
        DraftPreviewChanged?.Invoke(this,new ArtStyleDraftEventArgs(null));
        RefreshCards(); UpdatePreview();
    }
    private void Cancel_Click(object sender,RoutedEventArgs e) => CancelDraft();
    private void Reset_Click(object sender,RoutedEventArgs e)
    {
        if (originalDraft==null) return; draft=originalDraft.Copy();
        refreshing=true; try { BuildEditorFields(); } finally { refreshing=false; } UpdatePreview();
    }
    private void SaveDraft_Click(object sender,RoutedEventArgs e)
    {
        if (draft==null || ArtStyleResolver.Validate(draft).Count!=0) return;
        draft.Name=draft.Name.Trim();
        try
        {
            var saved=ArtStyleService.Save(Config,draft); selectedId=saved.Id;
            ArtStyleService.Select(Config,saved.Id,FollowBox.IsChecked==true);
            CancelDraft(); Persist("Saved");
        }
        catch (InvalidDataException ex) { StatusText.Text=ArtStyleText.Get("FileError")+ex.Message; }
    }
    private void Rename_Click(object sender,RoutedEventArgs e)
    {
        var profile=ArtStyleCatalog.Find(Config,selectedId); if (profile==null || ArtStyleCatalog.IsBuiltIn(profile.Id)) return;
        var dialog=new InputDialog(ArtStyleText.Get("Rename"),ArtStyleText.Get("Name"),profile.Name,s=>(!string.IsNullOrWhiteSpace(s)&&s.Length<=80,ArtStyleText.Get("Invalid")+ArtStyleText.Get("Name"))) { Owner=Window.GetWindow(this) };
        if (dialog.ShowDialog()!=true) return;
        profile.Name=dialog.InputText; ArtStyleService.Save(Config,profile); Persist("Saved");
    }
    private void Delete_Click(object sender,RoutedEventArgs e)
    {
        if (ArtStyleCatalog.IsBuiltIn(selectedId)) return;
        ArtStyleService.Delete(Config,selectedId); selectedId=ArtStyleCatalog.DefaultId; Persist("Deleted");
    }
    private void Import_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog { Filter=ArtStyleText.Get("ThemeFilter") };
        if (dialog.ShowDialog(Window.GetWindow(this))!=true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length>ArtStyleService.MaxFileBytes) throw new InvalidDataException("file: 64 KiB");
            var imported=ArtStyleService.Import(Config,File.ReadAllText(dialog.FileName)); selectedId=imported.Id;
            Persist("Imported");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { StatusText.Text=ArtStyleText.Get("FileError")+ex.Message; }
    }
    private void Export_Click(object sender,RoutedEventArgs e)
    {
        var profile=ArtStyleCatalog.Find(Config,selectedId); if (profile==null) return;
        var dialog=new SaveFileDialog { Filter=ArtStyleText.Get("ThemeFilter"),FileName="StarPie-theme.starpie-theme.json",DefaultExt=".starpie-theme.json",AddExtension=true };
        if (dialog.ShowDialog(Window.GetWindow(this))!=true) return;
        try { File.WriteAllText(dialog.FileName,ArtStyleService.Export(profile)); StatusText.Text=ArtStyleText.Get("Exported"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { StatusText.Text=ArtStyleText.Get("FileError")+ex.Message; }
    }
    private void Persist(string message)
    {
        bool saved=ConfigManager.SaveConfig();
        ArtStyleService.NotifyChanged(); Refresh(); StatusText.Text=ArtStyleText.Get(saved?message:"Unsaved");
    }
}
