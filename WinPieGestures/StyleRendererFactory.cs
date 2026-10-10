namespace WinPieGestures;

public static class StyleRendererFactory
{
	public static IRadialStyleRenderer CreateRenderer(string style, AppConfig? config, bool isSubWheel = false)
	{
		if (config?.ArtStyleFollowsWheel == true && (!isSubWheel || !UsesIndependentSubAppearance(config)))
		{
			var art = ArtStyles.ArtStyleResolver.Resolve(config);
			if (art != null) return new ArtStyles.ArtStyleRenderer(art);
		}
		return CreateRenderer(style);
	}

	public static bool UsesIndependentSubAppearance(AppConfig config) => config.UseIndependentSubWheelTheme ||
		(!string.IsNullOrEmpty(config.SubWheelTheme) && config.SubWheelTheme != "FollowPrimary" && config.SubWheelTheme != config.Theme) ||
		(!string.IsNullOrEmpty(config.SubWheelUiStyle) && config.SubWheelUiStyle != "FollowPrimary" && config.SubWheelUiStyle != config.UiStyle);

	public static IRadialStyleRenderer CreateRenderer(string style)
	{
		if (string.IsNullOrEmpty(style))
		{
			return new ClassicRingRenderer();
		}
		return style.Trim() switch
		{
			"Glassmorphism" => new GlassmorphismRenderer(), 
			"CleanSectors" => new CleanSectorsRenderer(), 
			_ => new ClassicRingRenderer(), 
		};
	}
}
