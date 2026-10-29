using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WinPieGestures;

/// <summary>只为直接 EXE 启动创建普通权限进程；不修改已有进程或开启令牌特权。</summary>
internal sealed partial class StandardUserLaunchNative : StandardUserProcessLauncher.IBackend, IDisposable
{
    private SafeProcessHandle? _desktopProcess;
    private uint _desktopProcessId;
    private SafeAccessTokenHandle? _desktopToken;
    private SafeAccessTokenHandle? _currentToken;
    private SafeAccessTokenHandle? _linkedToken;

    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int cls, IntPtr buffer, int length, out int required);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateTokenEx(SafeAccessTokenHandle existing, uint access, IntPtr attributes, int level, int type, out SafeAccessTokenHandle token);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool CreateEnvironmentBlock(out IntPtr environment, SafeAccessTokenHandle token, bool inherit);
    [DllImport("userenv.dll")] private static extern bool DestroyEnvironmentBlock(IntPtr environment);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(SafeAccessTokenHandle token, uint logonFlags, string application, StringBuilder commandLine, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        internal uint Size;
        internal string? Reserved, Desktop, Title;
        internal uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        internal ushort ShowWindow, ReservedSize;
        internal IntPtr ReservedData, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        internal IntPtr Process, Thread;
        internal uint ProcessId, ThreadId;
    }

    public bool TryGetDesktopIdentity(out StandardUserTokenIdentity identity)
    {
        identity = default;
        IntPtr shell = GetShellWindow();
        if (shell == IntPtr.Zero) return false;
        GetWindowThreadProcessId(shell, out uint pid);
        if (_desktopToken == null)
        {
            _desktopProcess = OpenProcess(0x1000, false, pid);
            if (_desktopProcess.IsInvalid || !OpenProcessToken(_desktopProcess.DangerousGetHandle(), 0x0008, out _desktopToken)) return false;
            _desktopProcessId = pid;
        }
        if (pid != _desktopProcessId) return false; // Shell 更换后不使用旧快照；进程句柄在本次调用内保持存活。
        return TryReadIdentity(_desktopToken, out identity);
    }
    public bool TryGetCurrentIdentity(out StandardUserTokenIdentity identity)
    {
        identity = default;
        if (_currentToken == null && !OpenProcessToken(GetCurrentProcess(), 0x000A, out _currentToken)) return false;
        return TryReadIdentity(_currentToken, out identity);
    }

    public bool TryGetLinkedDesktopIdentity(out StandardUserTokenIdentity identity)
    {
        identity = default;
        if (_desktopToken == null) return false;
        if (_linkedToken == null)
        {
            if (!DesktopShellToken.TryQueryTokenInformation(_desktopToken, 19, IntPtr.Size, GetTokenInformation, out IntPtr buffer)) return false;
            try
            {
                IntPtr handle = Marshal.ReadIntPtr(buffer);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;
                using var linked = new SafeAccessTokenHandle(handle);
                // 复制为明确的主令牌，只申请启动与读取必需的访问权。
                if (!DuplicateTokenEx(linked, 0x000B, IntPtr.Zero, 2, 1, out _linkedToken)) return NativeFailure("DuplicateTokenEx");
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return TryReadIdentity(_linkedToken, out identity);
    }

    internal static bool TryReadIdentity(SafeAccessTokenHandle token, out StandardUserTokenIdentity identity)
    {
        identity = default;
        if (!DesktopShellToken.TryGetStandardUserStatus(token, GetTokenInformation, out bool standard)) return false;
        IntPtr user = IntPtr.Zero, session = IntPtr.Zero;
        try
        {
            if (!DesktopShellToken.TryQueryTokenInformation(token, 1, IntPtr.Size, GetTokenInformation, out user)) return false;
            IntPtr sid = Marshal.ReadIntPtr(user);
            if (sid == IntPtr.Zero) return false;
            string userSid = new SecurityIdentifier(sid).Value;
            if (!DesktopShellToken.TryQueryTokenInformation(token, 12, sizeof(int), GetTokenInformation, out session)) return false;
            int sessionId = Marshal.ReadInt32(session);
            if (sessionId < 0) return false;
            identity = new StandardUserTokenIdentity(userSid, sessionId, standard);
            return true;
        }
        finally
        {
            if (session != IntPtr.Zero) Marshal.FreeHGlobal(session);
            if (user != IntPtr.Zero) Marshal.FreeHGlobal(user);
        }
    }

    public bool StartCurrent(StandardUserLaunchRequest request) => Start(request, _currentToken, false);
    public bool StartLinked(StandardUserLaunchRequest request) => Start(request, _linkedToken, true);

    private bool Start(StandardUserLaunchRequest request, SafeAccessTokenHandle? token, bool useLinkedToken)
    {
        lock (NativeCreationGate) return StartCore(request, token, useLinkedToken);
    }

    private bool StartCore(StandardUserLaunchRequest request, SafeAccessTokenHandle? token, bool useLinkedToken)
    {
        if (token == null || !TryReadIdentity(token, out var target) || !target.IsStandardUser ||
            !TryGetDesktopIdentity(out var desktop) || !TryGetCurrentIdentity(out var current) ||
            target.UserSid != desktop.UserSid || target.SessionId != desktop.SessionId || current.SessionId != desktop.SessionId)
            return false;
        if (!useLinkedToken && !current.IsStandardUser) return false;
        if (!CreateEnvironmentBlock(out IntPtr environment, token, false)) return NativeFailure("CreateEnvironmentBlock");

        var startup = CreateStartupInfo(request);
        ProcessInformation process = default;
        bool created = false, resumed = false;
        try
        {
            // 先挂起创建，核实子进程令牌后才允许目标代码运行。
            uint flags = CreationFlags(request);
            var commandLine = new StringBuilder(request.CommandLine);
            created = useLinkedToken
                ? CreateProcessWithTokenW(token, 0, request.FileName, commandLine, flags, environment, request.WorkingDirectory, ref startup, out process)
                : CreateProcessW(request.FileName, commandLine, IntPtr.Zero, IntPtr.Zero, false, flags, environment, request.WorkingDirectory, ref startup, out process);
            if (!created) return NativeFailure(useLinkedToken ? "CreateProcessWithTokenW" : "CreateProcessW");
            if (!OpenProcessToken(process.Process, 0x0008, out SafeAccessTokenHandle child)) return NativeFailure("Open child token");
            using (child)
            {
                if (!TryReadIdentity(child, out var actual) || !IsAllowedChildIdentity(actual, desktop))
                    return false;
            }
            uint previousSuspendCount = ResumeThread(process.Thread);
            if (previousSuspendCount == uint.MaxValue) return NativeFailure("ResumeThread");
            if (previousSuspendCount != 1)
            {
                AppLogger.LogWarn($"Standard-user native launch: unexpected suspend count {previousSuspendCount}.");
                return false;
            }
            resumed = true;
            return true;
        }
        finally
        {
            if (created && process.Process != IntPtr.Zero && !resumed && !TerminateProcess(process.Process, 1)) NativeFailure("Terminate suspended child");
            if (created && process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
            if (created && process.Process != IntPtr.Zero) CloseHandle(process.Process);
            DestroyEnvironmentBlock(environment);
        }
    }

    internal static StartupInfo CreateStartupInfo(StandardUserLaunchRequest request) => new()
    {
        Size = (uint)Marshal.SizeOf<StartupInfo>(), Desktop = "winsta0\\default",
        Flags = 1, ShowWindow = (ushort)request.ShowCommand,
    };

    internal static uint CreationFlags(StandardUserLaunchRequest request) =>
        0x00000404u | (request.Hidden ? 0x08000000u : 0u); // UNICODE_ENVIRONMENT | SUSPENDED | 可选 NO_WINDOW

    internal static bool IsAllowedChildIdentity(StandardUserTokenIdentity actual, StandardUserTokenIdentity expected) =>
        actual.IsStandardUser && !string.IsNullOrEmpty(expected.UserSid) && expected.SessionId >= 0 &&
        actual.UserSid == expected.UserSid && actual.SessionId == expected.SessionId;
    private static bool NativeFailure(string operation)
    {
        int error = Marshal.GetLastWin32Error();
        AppLogger.LogWarn($"Standard-user native launch: {operation} failed (Win32 {error}).");
        return false;
    }

    public void Dispose()
    {
        _trackedToken?.Dispose();
        _linkedToken?.Dispose();
        _currentToken?.Dispose();
        _desktopToken?.Dispose();
        _desktopProcess?.Dispose();
    }
}
