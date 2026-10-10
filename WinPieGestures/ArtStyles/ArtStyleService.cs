using System.IO;
using System.Text;
using System.Text.Json;

namespace WinPieGestures.ArtStyles;

public sealed class ArtStyleService
{
	private ArtStyleService() { }
	public static ArtStyleService Instance { get; } = new();
	public event EventHandler? Changed;

	/// <summary>UI-only visual refresh; callers decide when to persist. Weak listeners do not retain closed settings.</summary>
	public static void NotifyChanged()
	{
		var app = System.Windows.Application.Current;
		if (app != null && !app.Dispatcher.CheckAccess()) { app.Dispatcher.Invoke(NotifyChanged); return; }
		ConfigManager.MarkConfigurationChanged();
		if (app != null) foreach (System.Windows.Window window in app.Windows.Cast<System.Windows.Window>().ToArray())
		{
			if (window is RadialWindow || !AppThemeManager.IsHostThemeWindow(window)) continue;
			AppThemeManager.ApplyTheme(window, ConfigManager.CurrentConfig.AppTheme);
		}
		Instance.Changed?.Invoke(Instance, EventArgs.Empty);
	}
    public const int MaxFileBytes = 65536;
    private const int MaxUserThemes = 128;
    private static readonly JsonSerializerOptions FileOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true, MaxDepth = 16 };

    private sealed class ThemeFile
    {
        public int SchemaVersion { get; set; }
        public ArtStyleProfile? Theme { get; set; }
    }

    public static void Select(AppConfig config, string id, bool followWheel)
    {
        var profile = ArtStyleCatalog.Find(config, id);
        RequireValid(profile);
        config.SelectedArtStyleId = id;
        config.ArtStyleFollowsWheel = followWheel;
    }

    public static ArtStyleProfile Save(AppConfig config, ArtStyleProfile profile)
    {
        RequireValid(profile);
        if (ArtStyleCatalog.IsBuiltIn(profile.Id)) throw new InvalidDataException("id: built-in");
        config.CustomArtStyles ??= [];
        var copy = profile.Copy();
        int index = config.CustomArtStyles.FindIndex(p => p?.Id == copy.Id);
        if (index >= 0) config.CustomArtStyles[index] = copy;
        else
        {
            if (config.CustomArtStyles.Count >= MaxUserThemes) throw new InvalidDataException("themes: limit 128");
            config.CustomArtStyles.Add(copy);
        }
        return copy.Copy();
    }

    public static void Delete(AppConfig config, string id)
    {
        if (ArtStyleCatalog.IsBuiltIn(id)) throw new InvalidDataException("id: built-in");
        config.CustomArtStyles?.RemoveAll(p => p?.Id == id);
        if (config.SelectedArtStyleId == id) config.SelectedArtStyleId = ArtStyleCatalog.DefaultId;
    }

    public static ArtStyleProfile Import(AppConfig config, string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxFileBytes) throw new InvalidDataException("file: limit 64 KiB");
        ThemeFile? file;
        try { file = JsonSerializer.Deserialize<ThemeFile>(json.TrimStart('\uFEFF'), FileOptions); }
        catch (JsonException e) { throw new InvalidDataException("JSON: " + e.Path, e); }
        if (file?.SchemaVersion != 1) throw new InvalidDataException("schemaVersion: 1");
        RequireValid(file.Theme);
        // Every import is independent, including built-in IDs and collisions with existing user themes.
        var profile = file.Theme!.Copy();
        profile.Id = "user-" + Guid.NewGuid().ToString("N");
        return Save(config, profile);
    }

    public static string Export(ArtStyleProfile profile)
    {
        RequireValid(profile);
        return JsonSerializer.Serialize(new ThemeFile { SchemaVersion = 1, Theme = profile.Copy() }, FileOptions);
    }

    private static void RequireValid(ArtStyleProfile? profile)
    {
        var errors = ArtStyleResolver.Validate(profile);
        if (errors.Count != 0) throw new InvalidDataException(string.Join(", ", errors));
    }
}
