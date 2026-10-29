using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WinPieGestures;

internal sealed partial class StandardUserLaunchNative : StandardUserProcessLauncher.ITrackedBackend
{
    // 只覆盖本模块创建可继承标准句柄的短窗口；旧的原生 bool 入口同样使用此锁。
    private static readonly object NativeCreationGate = new();
    private SafeAccessTokenHandle? _trackedToken;

    [StructLayout(LayoutKind.Sequential)]
    private struct PipeSecurityAttributes
    {
        internal int Length;
        internal IntPtr Descriptor;
        [MarshalAs(UnmanagedType.Bool)] internal bool Inherit;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref PipeSecurityAttributes attributes, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, ref PipeSecurityAttributes attributes,
        uint creation, uint flags, IntPtr template);

    public OwnedChildProcess StartTracked(ProcessStartInfo info, StandardUserTokenSource source)
    {
        if (!TryGetDesktopIdentity(out var desktop) || !TryGetCurrentIdentity(out var current) ||
            current.UserSid != desktop.UserSid || current.SessionId != desktop.SessionId)
            throw Refused("Host/desktop identity changed before tracked creation.");
        if (source == StandardUserTokenSource.Current)
        {
            if (!current.IsStandardUser) throw Refused("Current process is elevated.");
            return OwnedChildProcess.StartManaged(info);
        }

        SafeAccessTokenHandle token;
        if (source == StandardUserTokenSource.Desktop)
        {
            if (!desktop.IsStandardUser || _desktopProcess == null ||
                !OpenProcessToken(_desktopProcess.DangerousGetHandle(), 0x000A, out SafeAccessTokenHandle desktopSource))
                throw Refused("No standard desktop token available for tracked creation.");
            using (desktopSource)
            {
                if (!DuplicateTokenEx(desktopSource, 0x000B, IntPtr.Zero, 2, 1, out _trackedToken))
                    throw NativeError("Duplicate standard desktop token");
            }
            token = _trackedToken;
        }
        else if (source == StandardUserTokenSource.LinkedDesktop)
        {
            if (desktop.IsStandardUser || !TryGetLinkedDesktopIdentity(out var linked) ||
                !IsAllowedChildIdentity(linked, desktop) || _linkedToken == null)
                throw Refused("No standard linked desktop token available for tracked creation.");
            token = _linkedToken;
        }
        else throw Refused("Unknown tracked token source.");

        if (!TryReadIdentity(token, out var target) || !IsAllowedChildIdentity(target, desktop))
            throw Refused("Target token identity did not match the expected standard user.");
        lock (NativeCreationGate) return StartTrackedCore(info, token, desktop);
    }

    private OwnedChildProcess StartTrackedCore(ProcessStartInfo info, SafeAccessTokenHandle token, StandardUserTokenIdentity desktop)
    {
        if (!CreateEnvironmentBlock(out IntPtr environment, token, false)) throw NativeError("CreateEnvironmentBlock");
        SafeFileHandle? outputRead = null, outputWrite = null, errorRead = null, errorWrite = null, input = null;
        StreamReader? outputReader = null, errorReader = null;
        Process? managedProcess = null;
        OwnedChildProcess? owner = null;
        ProcessInformation process = default;
        bool created = false, resumed = false;
        try
        {
            CreateOutputPipe(out outputRead, out outputWrite);
            CreateOutputPipe(out errorRead, out errorWrite);
            var attributes = InheritableAttributes();
            input = CreateFileW("NUL", 0x80000000, 3, ref attributes, 3, 0, IntPtr.Zero);
            if (input.IsInvalid) throw NativeError("Open standard input");
            var request = new StandardUserLaunchRequest(info.FileName, info.Arguments, info.WorkingDirectory,
                info.CreateNoWindow || info.WindowStyle == ProcessWindowStyle.Hidden ? 0 : 1);
            StartupInfo startup = CreateStartupInfo(request);
            startup.Flags |= 0x100; // STARTF_USESTDHANDLES：所有句柄均为显式准备的可继承标准句柄。
            startup.StandardInput = input.DangerousGetHandle();
            startup.StandardOutput = outputWrite.DangerousGetHandle();
            startup.StandardError = errorWrite.DangerousGetHandle();
            var command = new StringBuilder(StandardUserProcessLauncher.QuoteArgument(info.FileName) +
                (info.Arguments.Length == 0 ? "" : " " + info.Arguments));
            created = CreateProcessWithTokenW(token, 0, info.FileName, command, CreationFlags(request), environment,
                info.WorkingDirectory, ref startup, out process);
            if (!created) throw NativeError("CreateProcessWithTokenW (tracked)");
            if (!OpenProcessToken(process.Process, 0x0008, out SafeAccessTokenHandle child)) throw NativeError("Open tracked child token");
            using (child)
                if (!TryReadIdentity(child, out var actual) || !IsAllowedChildIdentity(actual, desktop))
                    throw Refused("Tracked child did not run as the expected standard user.");

            // 在子进程仍挂起时接管句柄及读取端。任何失败都先终止本次子进程，再清理资源。
            managedProcess = Process.GetProcessById(checked((int)process.ProcessId));
            _ = managedProcess.SafeHandle;
            outputReader = TakeReader(ref outputRead, info.StandardOutputEncoding ?? Encoding.UTF8);
            errorReader = TakeReader(ref errorRead, info.StandardErrorEncoding ?? Encoding.UTF8);
            owner = new OwnedChildProcess(managedProcess, outputReader, errorReader);
            managedProcess = null;
            outputReader = errorReader = null;
            if (ResumeThread(process.Thread) != 1) throw NativeError("Resume tracked child");
            resumed = true;
            OwnedChildProcess result = owner;
            owner = null;
            return result;
        }
        finally
        {
            if (created && process.Process != IntPtr.Zero && !resumed) _ = TerminateProcess(process.Process, 1);
            owner?.Dispose();
            managedProcess?.Dispose();
            outputReader?.Dispose();
            errorReader?.Dispose();
            outputRead?.Dispose();
            errorRead?.Dispose();
            outputWrite?.Dispose();
            errorWrite?.Dispose();
            input?.Dispose();
            if (process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
            if (process.Process != IntPtr.Zero) CloseHandle(process.Process);
            DestroyEnvironmentBlock(environment);
        }
    }

    private static PipeSecurityAttributes InheritableAttributes() => new()
    { Length = Marshal.SizeOf<PipeSecurityAttributes>(), Inherit = true };

    private static void CreateOutputPipe(out SafeFileHandle read, out SafeFileHandle write)
    {
        var attributes = InheritableAttributes();
        if (!CreatePipe(out read, out write, ref attributes, 0))
        {
            read?.Dispose(); write?.Dispose();
            throw NativeError("Create output pipe");
        }
        if (!SetHandleInformation(read, 1, 0))
        {
            read.Dispose(); write.Dispose();
            throw NativeError("Clear read-pipe inheritance");
        }
    }

    private static StreamReader TakeReader(ref SafeFileHandle? handle, Encoding encoding)
    {
        var stream = new FileStream(handle!, FileAccess.Read, 4096, isAsync: false);
        handle = null;
        try { return new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true); }
        catch { stream.Dispose(); throw; }
    }

    private static InvalidOperationException Refused(string reason)
    {
        AppLogger.LogWarn("Tracked native launch refused: " + reason);
        return new InvalidOperationException(I18n.T("PluginsProcessLaunchDenied"));
    }

    private static InvalidOperationException NativeError(string operation)
    {
        int error = Marshal.GetLastWin32Error();
        AppLogger.LogWarn($"Tracked native launch: {operation} failed (Win32 {error}).");
        return new InvalidOperationException(I18n.T("PluginsProcessLaunchDenied"), new Win32Exception(error));
    }
}