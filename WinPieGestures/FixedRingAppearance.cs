namespace WinPieGestures;

public sealed class FixedOuterRingAppearance
{
    public string? Shape { get; set; }
    public double? Thickness { get; set; }
    public double? SectorGap { get; set; }
    public double? RingGap { get; set; }
    public double? CornerRadius { get; set; }

    public FixedRingAppearanceTier? Level3 { get; set; }
    public FixedRingAppearanceTier? Level4 { get; set; }
    public FixedRingAppearanceTier? Level5 { get; set; }

    internal FixedRingAppearanceTier? GetTier(int level) => level switch
    {
        3 => Level3,
        4 => Level4,
        5 => Level5,
        _ => null
    };
}

public sealed class FixedRingAppearanceTier
{
    public double? Thickness { get; set; }
    public double? SectorGap { get; set; }
}

public sealed class FixedHoneycombAppearance
{
    public string? Shape { get; set; }
    public double? CellWidth { get; set; }
    public double? CellGap { get; set; }
    public double? CornerRadius { get; set; }
    public double? CoreRadius { get; set; }
}
