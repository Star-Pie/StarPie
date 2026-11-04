using System;
using System.Threading.Tasks;

namespace WinPieGestures;

internal sealed class AppShutdownSequence
{
    private readonly object _gate = new();
    private Task? _completion;

    internal Task RunAsync(Func<Task> cleanup, Action shutdown, Action<Exception> report)
    {
        lock (_gate)
        {
            return _completion ??= RunCoreAsync(cleanup, shutdown, report);
        }
    }

    private static async Task RunCoreAsync(Func<Task> cleanup, Action shutdown, Action<Exception> report)
    {
        // Publish the shared task before any cleanup callback can re-enter Exit.
        await Task.Yield();
        try { await cleanup(); }
        catch (Exception ex) { try { report(ex); } catch { } }
        finally { shutdown(); }
    }
}
