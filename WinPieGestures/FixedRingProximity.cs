using System.Windows;

namespace WinPieGestures;

internal readonly record struct FixedRingVisibility(ulong Third, ulong Fourth, ulong Fifth)
{
    internal int VisibleDepth => Fifth != 0 ? 5 : Fourth != 0 ? 4 : Third != 0 ? 3 : 2;

    internal ulong At(int level) => level switch
    {
        3 => Third,
        4 => Fourth,
        5 => Fifth,
        _ => 0
    };

    internal bool Contains(int level, int index) => (uint)index < 64 && (At(level) & (1UL << index)) != 0;
}

// Runtime and the appearance preview use the same screen-space outlines and reveal rules.
internal sealed class FixedRingProximity
{
    private readonly FixedRingGeometry _geometry;
    private readonly int _thirdCount, _fourthCount, _fifthCount;

    internal FixedRingVisibility Configured { get; }

    internal FixedRingProximity(FixedRingGeometry geometry, FixedRingMenu menu, int depth,
        HashSet<(int Level, int Index)> configured)
    {
        _geometry = geometry;
        _thirdCount = depth >= 3 ? menu.GetCount(3) : 0;
        _fourthCount = depth >= 4 ? menu.GetCount(4) : 0;
        _fifthCount = depth >= 5 ? menu.GetCount(5) : 0;
        Configured = new(ReadTier(3, _thirdCount), ReadTier(4, _fourthCount), ReadTier(5, _fifthCount));

        ulong ReadTier(int level, int count)
        {
            ulong bits = 0;
            for (int index = 0; index < count; index++)
                if (configured.Contains((level, index))) bits |= 1UL << index;
            return bits;
        }
    }

    internal FixedRingVisibility Around(Point point, FixedRingVisibility previous) => new(
        _geometry.NearSlots(point, 3, _thirdCount, Configured.Third, previous.Third),
        _geometry.NearSlots(point, 4, _fourthCount, Configured.Fourth, previous.Fourth),
        _geometry.NearSlots(point, 5, _fifthCount, Configured.Fifth, previous.Fifth));
}
