using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace WinPieGestures;

/// <summary>只在专用诊断候选中记录准星松开时的窗口树；不参与目标选择。</summary>
internal static class WindowCaptureDiagnostics
{
#if WINDOW_CAPTURE_DIAGNOSTICS
    internal const bool EnabledInThisBuild = true;
#else
    internal const bool EnabledInThisBuild = false;
#endif
    private const int MaximumParentDepth = 32;

    [Conditional("WINDOW_CAPTURE_DIAGNOSTICS")]
    internal static void Record(int x, int y, nint hit, nint root, string result,
        string selectedProcessName = "", string selectedPath = "", uint selectedProcessId = 0)
    {
        try
        {
            // 在准星松开时取快照，避免后台延迟采样时窗口或文档标签已经切换。
            var (handles, stopReason) = GetParentChain(hit, root, hwnd => GetAncestor(hwnd, 1)); // GA_PARENT，不沿 owner 链。
            var windows = new List<WindowSnapshot>(handles.Length);
            WindowSnapshot? rootSnapshot = null;
            foreach (nint hwnd in handles)
            {
                WindowSnapshot snapshot = ReadWindow(hwnd);
                windows.Add(snapshot);
                if (hwnd == root) rootSnapshot = snapshot;
            }
            rootSnapshot ??= ReadWindow(root);
            string json = JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                CaptureId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTimeOffset.Now,
                DiagnosticBuild = EnabledInThisBuild,
                BuildDirectory = AppContext.BaseDirectory,
                HostProcessId = Environment.ProcessId,
                Cursor = new { X = x, Y = y },
                HitHwnd = Hex(hit),
                ResolvedRootHwnd = Hex(root),
                ParentChainOrder = "hit-to-root",
                ParentChainStop = stopReason,
                ParentChain = windows,
                Root = rootSnapshot,
                Result = result,
                SelectedProcessId = selectedProcessId,
                SelectedProcessName = selectedProcessName,
                SelectedExecutablePath = selectedPath
            }, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            // 复用异步日志队列；不在移动事件、轮盘热路径或低级钩子中采样/写文件。
            AppLogger.LogInfo("[WindowCaptureTrace] " + json);
        }
        catch (Exception ex)
        {
            // 诊断失败不能阻止原有窗口选择。
            try { AppLogger.LogWarn("[WindowCaptureTrace] failed: " + ex.GetType().Name + ": " + ex.Message); }
            catch (Exception) { }
        }
    }

    internal static (nint[] Handles, string StopReason) GetParentChain(nint hit, nint root,
        Func<nint, nint> readParent)
    {
        var chain = new List<nint>();
        var seen = new HashSet<nint>();
        nint current = hit;
        while (current != IntPtr.Zero && chain.Count < MaximumParentDepth)
        {
            if (!seen.Add(current)) return (chain.ToArray(), "cycle");
            chain.Add(current);
            if (current == root) return (chain.ToArray(), "root");
            current = readParent(current);
        }
        return (chain.ToArray(), current == IntPtr.Zero ? "null" : "depth-limit");
    }

    private static WindowSnapshot ReadWindow(nint hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return new WindowSnapshot(Hex(hwnd), false, 0, 0, "", "", 0, "", "");

        uint threadId = GetWindowThreadProcessId(hwnd, out uint processId);
        string path = "";
        int imageQueryError = 0;
        if (processId != 0)
        {
            nint process = OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION
            if (process == IntPtr.Zero) imageQueryError = Marshal.GetLastWin32Error();
            else
            {
                try
                {
                    var image = new StringBuilder(32768);
                    uint length = (uint)image.Capacity;
                    if (QueryFullProcessImageName(process, 0, image, ref length)) path = image.ToString();
                    else imageQueryError = Marshal.GetLastWin32Error();
                }
                finally { CloseHandle(process); }
            }
        }
        var title = new StringBuilder(1024);
        var className = new StringBuilder(256);
        GetWindowText(hwnd, title, title.Capacity);
        GetClassName(hwnd, className, className.Capacity);
        return new WindowSnapshot(Hex(hwnd), true, processId, threadId, Path.GetFileName(path), path,
            imageQueryError, className.ToString(), title.ToString());
    }

    private static string Hex(nint hwnd) => "0x" + ((nuint)hwnd).ToString("X");

    private sealed record WindowSnapshot(string Hwnd, bool Exists, uint ProcessId, uint ThreadId,
        string ProcessName, string ExecutablePath, int ImageQueryError, string ClassName, string Title);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder name, int maximum);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref uint length);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
