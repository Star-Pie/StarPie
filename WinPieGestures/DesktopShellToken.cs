using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinPieGestures;

/// <summary>降权前确认桌面 Shell 的令牌确实不是管理员／高完整性；探测失败时拒绝降权调用。</summary>
internal static class DesktopShellToken
{
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, IntPtr information, int length, out int returnLength);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

    internal static bool IsStandardUser()
    {
        IntPtr shell = GetShellWindow();
        if (shell == IntPtr.Zero) return false;
        GetWindowThreadProcessId(shell, out uint pid);
        using SafeProcessHandle process = OpenProcess(0x1000, false, pid); // QUERY_LIMITED_INFORMATION
        if (process.IsInvalid || !OpenProcessToken(process, 0x0008, out SafeAccessTokenHandle token)) return false;
        using (token)
        {
            GetTokenInformation(token, 25, IntPtr.Zero, 0, out int length); // TokenIntegrityLevel
            if (length <= 0 || length > 65536) return false;
            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, 25, buffer, length, out _)) return false;
                IntPtr sid = Marshal.ReadIntPtr(buffer);
                if (sid == IntPtr.Zero) return false;
                byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                if (count == 0) return false;
                int integrity = Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
                if (integrity >= 0x3000) return false; // HIGH or SYSTEM
                if (!GetTokenInformation(token, 20, buffer, length, out _)) return false; // TokenElevation
                return Marshal.ReadInt32(buffer) == 0;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
}
