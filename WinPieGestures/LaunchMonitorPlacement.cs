using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using StarPie.Plugin;
using PhysicalPoint = System.Windows.Point;
using PixelPoint = System.Drawing.Point;

namespace WinPieGestures;

internal static class LaunchMonitorPlacement
{
    private const string LaunchPlugin = "starpie.builtin.launch";
    private static readonly AsyncLocal<Invocation?> Current = new();

    // The official plugin calls the existing launch service from its invocation Task.
    // Keep the device host-owned, scoped to this action, and consumable only once.
    private sealed class Invocation(string? device, Invocation? previous) : IDisposable
    {
        private string? _device = device;
        internal string? Take() => Interlocked.Exchange(ref _device, null);
        public void Dispose() { Take(); Current.Value = previous; }
    }

    internal static IDisposable? BeginInvocation(string? device)
    {
        var previous = Current.Value;
        if (device == null && previous == null) return null;
        return Current.Value = new Invocation(device, previous);
    }

    internal static string? TakeForPlugin(string pluginId) =>
        string.Equals(pluginId, LaunchPlugin, StringComparison.OrdinalIgnoreCase) ? Current.Value?.Take() : null;

    internal static bool IsSupportedAction(ActionItem? action) =>
        string.Equals(action?.Type, "Launch", StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(action?.Type, "Plugin", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(action?.PluginActionRef?.FullId, LaunchPlugin + ".launch", StringComparison.OrdinalIgnoreCase));

    internal static string? Capture(ActionItem? action, PhysicalPoint? eventPoint, Func<PhysicalPoint?, string?>? lookupMonitor = null)
    {
        if (action?.LaunchOnCursorMonitor != true || !IsSupportedAction(action)) return null;
        if (lookupMonitor != null) return lookupMonitor(eventPoint);
        try
        {
            PixelPoint point;
            if (eventPoint is { } p) point = new PixelPoint((int)Math.Round(p.X), (int)Math.Round(p.Y));
            else if (!GetCursorPos(out point)) return null;
            var screen = Screen.FromPoint(point);
            return screen.Bounds.Contains(point) ? screen.DeviceName : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { return null; }
    }

    internal static bool CanTrack(string? device, string path, ProcessLaunchMode mode, bool shellTarget)
    {
        if (device == null || mode != ProcessLaunchMode.Default || shellTarget) return false;
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) || extension.Length == 0;
    }

    // A slower activation of the same application must not move it back after a later gesture.
    internal sealed class PlacementRequest : IDisposable
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, PlacementRequest> Latest =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly string _key;
        internal PlacementRequest(string key) { _key = key; Latest[key] = this; }
        internal bool IsCurrent => Latest.TryGetValue(_key, out var current) && ReferenceEquals(current, this);
        public void Dispose() => Latest.TryRemove(new KeyValuePair<string, PlacementRequest>(_key, this));
    }

    internal enum RecallAction { Activate, Restore, Minimize }

    internal static RecallAction GetRecallAction(WindowSnapshot window, string device, string currentDevice)
    {
        if (window.IsIconic) return RecallAction.Restore;
        return window.IsForeground && string.Equals(device, currentDevice, StringComparison.OrdinalIgnoreCase)
            ? RecallAction.Minimize : RecallAction.Activate;
    }

    internal static bool StartWithPlacement(ProcessStartInfo info, string device)
    {
        string? image = ResolveImage(info.FileName);
        var request = new PlacementRequest(image ?? info.FileName);
        var owners = new List<Process>();
        bool observing = false;
        try
        {
            if (string.IsNullOrWhiteSpace(info.Arguments) && image != null)
                HoldExistingInstances(image, owners);

            var priorPids = owners.Select(p => p.Id).ToHashSet();
            var existing = FindUserWindow(owners, allowIconic: true);
            if (existing != null)
            {
                if (!request.IsCurrent || !StillOwnedBy(existing.Handle, existing.ProcessId)) return false;
                var action = GetRecallAction(existing, device, Screen.FromHandle(existing.Handle).DeviceName);
                AppLogger.LogDebug($"Monitor launch: existing window {action} (pid={existing.ProcessId}).");
                if (action == RecallAction.Minimize) return ShowWindowAsync(existing.Handle, 6);
                if (action == RecallAction.Restore && !ShowWindowAsync(existing.Handle, 9)) return false;
            }
            else
            {
                // A tray application may wake its original process and immediately exit this second launch.
                // Only instances proven to belong to this executable before dispatch may receive that handoff.
                if (owners.Count != 0) AppLogger.LogDebug("Monitor launch: no visible window; requesting normal application activation.");
                DateTime before = DateTime.UtcNow;
                Process? started = Process.Start(info);
                if (started != null && !TryHoldProcess(started, image ?? info.FileName, before, owners))
                    AppLogger.LogDebug("Monitor launch: returned process exited or did not match the requested executable.");
            }
            if (owners.Count != 0)
            {
                _ = Task.Run(() => ObserveAsync(owners, existing, priorPids, request, device));
                observing = true;
            }
            return true;
        }
        finally
        {
            if (!observing) Release(owners, request);
        }
    }

    private static string? ResolveImage(string path)
    {
        if (Path.GetExtension(path).Length == 0) path += ".exe";
        if (Path.IsPathRooted(path) || path.Contains(Path.DirectorySeparatorChar) || path.Contains(Path.AltDirectorySeparatorChar))
            return Path.GetFullPath(path);
        var buffer = new System.Text.StringBuilder(32768);
        uint count = SearchPath(null, path, null, (uint)buffer.Capacity, buffer, 0);
        return count > 0 && count < buffer.Capacity ? buffer.ToString() : null;
    }

    private static void HoldExistingInstances(string image, List<Process> owners)
    {
        try
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(image)))
                TryHoldProcess(process, image, null, owners);
        }
        catch (Exception ex) when (IsObservationFailure(ex))
        {
            AppLogger.LogDebug($"Monitor launch: existing process lookup unavailable ({ex.GetType().Name}).");
        }
    }

    private static bool TryHoldProcess(Process process, string image, DateTime? startedAfter, List<Process> owners)
    {
        bool retained = false;
        try
        {
            // Pin the process identity during the observation, including when its original HWND is destroyed.
            _ = process.Handle;
            if (process.Id == Environment.ProcessId || process.HasExited || process.MainModule?.FileName is not { } actual ||
                !MatchesNewProcess(image, actual, process.StartTime.ToUniversalTime(), startedAfter ?? DateTime.MinValue) ||
                owners.Any(p => p.Id == process.Id)) return false;
            owners.Add(process);
            retained = true;
            return true;
        }
        catch (Exception ex) when (IsObservationFailure(ex)) { return false; }
        finally { if (!retained) process.Dispose(); }
    }

    internal static bool MatchesNewProcess(string requested, string actual, DateTime birth, DateTime before)
    {
        if (birth < before) return false;
        if (Path.GetExtension(requested).Length == 0) requested += ".exe";
        return Path.IsPathFullyQualified(requested)
            ? string.Equals(Path.GetFullPath(requested), actual, StringComparison.OrdinalIgnoreCase)
            : string.Equals(Path.GetFileName(requested), Path.GetFileName(actual), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsObservationFailure(Exception ex) => ex is System.ComponentModel.Win32Exception
        or InvalidOperationException or IOException or ArgumentException or NotSupportedException;

    private static WindowSnapshot? FindUserWindow(List<Process> owners, bool allowIconic)
    {
        if (owners.Count == 0) return null;
        var ids = owners.Where(p => !p.HasExited).Select(p => p.Id).ToHashSet();
        if (ids.Count == 0) return null;
        var windows = ProcessWindowSelector.SnapshotWindows(ids);
        return SelectUserWindow(windows.Where(w => !IsCloaked(w.Handle)), ids, allowIconic);
    }

    internal static WindowSnapshot? SelectUserWindow(IEnumerable<WindowSnapshot> windows, HashSet<int> owners, bool allowIconic) =>
        ProcessWindowSelector.SelectCandidate(windows.Where(w => owners.Contains(w.ProcessId) && w.Owner == 0 &&
            (w.ExStyle & ProcessWindowSelector.WS_EX_TOOLWINDOW) == 0 &&
            w.IsVisible && (allowIconic || !w.IsIconic)));

    private static bool IsCloaked(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    // Wait for the same eligible user window twice; a launcher may momentarily expose a splash HWND.
    internal static async Task<WindowSnapshot?> WaitForStableWindowAsync(Func<bool> keepWaiting,
        Func<WindowSnapshot?> find, Func<Task> pause)
    {
        WindowSnapshot? last = null;
        while (keepWaiting())
        {
            var current = find();
            if (current != null && last?.Handle == current.Handle && last.ProcessId == current.ProcessId) return current;
            last = current;
            await pause().ConfigureAwait(false);
        }
        return null;
    }

    internal static bool IsRecallReplacement(WindowSnapshot? foreground, int pid) =>
        foreground?.IsForeground == true && IsOwnedMainWindow(foreground, pid);

    internal static bool ShouldActivate(bool current, bool recalledInstance, long elapsedMs) =>
        current && (recalledInstance || elapsedMs < 2150);

    private static async Task ObserveAsync(List<Process> owners, WindowSnapshot? existing, HashSet<int> priorPids,
        PlacementRequest request, string device)
    {
        try
        {
            var elapsed = Stopwatch.StartNew();
            WindowSnapshot? Find()
            {
                if (existing == null) return FindUserWindow(owners, allowIconic: false);
                var w = ProcessWindowSelector.CreateSnapshotFromHwnd(existing.Handle);
                if (w?.ProcessId == existing.ProcessId && w.IsVisible)
                    return IsOwnedMainWindow(w, existing.ProcessId) && !IsCloaked(existing.Handle) ? w : null;
                // Some applications replace their HWND while restoring. Only follow the new
                // foreground window of the original process; never unhide an arbitrary sibling.
                var replacement = ProcessWindowSelector.CreateSnapshotFromHwnd(GetForegroundWindow());
                return IsRecallReplacement(replacement, existing.ProcessId) && !IsCloaked(replacement!.Handle) ? replacement : null;
            }
            var window = await WaitForStableWindowAsync(
                () => request.IsCurrent && elapsed.Elapsed < TimeSpan.FromSeconds(12) && owners.Any(p => !p.HasExited),
                Find, () => Task.Delay(150)).ConfigureAwait(false);
            if (window == null)
            {
                if (request.IsCurrent) AppLogger.LogDebug("Monitor launch: no eligible visible window before process exit or timeout.");
                return;
            }
            if (!request.IsCurrent || !StillOwnedBy(window.Handle, window.ProcessId)) return;
            MoveToDevice(window.Handle, window.ProcessId, device);
            if (ShouldActivate(request.IsCurrent, priorPids.Contains(window.ProcessId), elapsed.ElapsedMilliseconds) &&
                StillOwnedBy(window.Handle, window.ProcessId))
            {
                bool activated = WindowTaskbarHelper.ActivateWindowConservative(window.Handle);
                AppLogger.LogDebug($"Monitor launch: activation {(activated ? "confirmed" : "declined")} (pid={window.ProcessId}).");
            }
        }
        catch (Exception ex) when (IsObservationFailure(ex))
        {
            AppLogger.LogDebug($"Launch monitor placement skipped ({ex.GetType().Name}).");
        }
        finally { Release(owners, request); }
    }

    private static void Release(List<Process> owners, PlacementRequest request)
    {
        request.Dispose();
        foreach (var process in owners) process.Dispose();
    }

    internal static bool IsOwnedMainWindow(WindowSnapshot? window, int pid) =>
        window != null && window.ProcessId == pid && window.IsVisible && !window.IsIconic && window.Owner == 0 &&
        (window.ExStyle & ProcessWindowSelector.WS_EX_TOOLWINDOW) == 0 && ProcessWindowSelector.IsValidUserWindowCandidate(window);

    internal static PixelPoint Translate(Rectangle window, Rectangle oldArea, Rectangle newArea) => new(
        Math.Clamp(newArea.Left + window.Left - oldArea.Left, newArea.Left, Math.Max(newArea.Left, newArea.Right - window.Width)),
        Math.Clamp(newArea.Top + window.Top - oldArea.Top, newArea.Top, Math.Max(newArea.Top, newArea.Bottom - window.Height)));

    private static void MoveToDevice(nint hwnd, int pid, string device)
    {
        var source = Screen.FromHandle(hwnd);
        if (string.Equals(source.DeviceName, device, StringComparison.OrdinalIgnoreCase)) return;
        var target = Screen.AllScreens.FirstOrDefault(m => string.Equals(m.DeviceName, device, StringComparison.OrdinalIgnoreCase));
        if (target == null || !GetWindowRect(hwnd, out var screenRect)) return;
        var position = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(hwnd, ref position)) return;

        var location = Translate(screenRect.ToRectangle(), source.WorkingArea, target.WorkingArea);
        const uint moveOnly = 0x0001 | 0x0004 | 0x0010 | 0x0200 | 0x4000; // NOSIZE, NOZORDER, NOACTIVATE, NOOWNERZORDER, ASYNCWINDOWPOS
        if (!StillOwnedBy(hwnd, pid)) return;
        if (position.ShowCommand != 3)
        {
            SetWindowPos(hwnd, 0, location.X, location.Y, 0, 0, moveOnly);
            return;
        }

        // WINDOWPLACEMENT uses workspace coordinates for top-level non-tool windows.
        // Keep the restored size/offset and move the maximized frame onto the new monitor.
        position.Normal = NativeRect.FromRectangle(TranslateRestored(position.Normal.ToRectangle(),
            source.Bounds, source.WorkingArea, target.Bounds, target.WorkingArea));
        position.Maximum = new PixelPoint(-1, -1);
        position.Flags |= 4; // WPF_ASYNCWINDOWPLACEMENT
        if (SetWindowPos(hwnd, 0, target.Bounds.Left, target.Bounds.Top, 0, 0, moveOnly) && StillOwnedBy(hwnd, pid))
            SetWindowPlacement(hwnd, ref position);
    }

    internal static Rectangle TranslateRestored(Rectangle restored, Rectangle sourceBounds, Rectangle sourceWork,
        Rectangle targetBounds, Rectangle targetWork)
    {
        restored.Offset(sourceWork.Left - sourceBounds.Left, sourceWork.Top - sourceBounds.Top);
        var dest = Translate(restored, sourceWork, targetWork);
        restored.Location = new PixelPoint(dest.X - (targetWork.Left - targetBounds.Left),
            dest.Y - (targetWork.Top - targetBounds.Top));
        return restored;
    }

    private static bool StillOwnedBy(nint hwnd, int pid) =>
        GetWindowThreadProcessId(hwnd, out uint owner) != 0 && owner == (uint)pid;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        internal readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
        internal static NativeRect FromRectangle(Rectangle r) => new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length, Flags, ShowCommand;
        public PixelPoint Minimum, Maximum;
        public NativeRect Normal;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint SearchPath(string? searchPath, string fileName, string? extension, uint size, System.Text.StringBuilder result, nint filePart);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PixelPoint point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(nint hwnd, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(nint hwnd, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int w, int h, uint flags);
}
