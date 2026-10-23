namespace WinPieGestures;

// UI-thread-owned revision: switching away and back must not revive an old result.
internal sealed class UpdateSourceState
{
    public string Source { get; private set; } = "github";
    public int Revision { get; private set; }

    public bool SelectSource(string? source)
    {
        string normalized = UpdateManager.NormalizeReleaseSource(source);
        if (normalized == Source) return false;
        Source = normalized;
        Revision++;
        return true;
    }

    public bool IsCurrent(int revision) => revision == Revision;
}
