using System;
using System.Threading;

namespace WinPieGestures;

internal enum InstanceHandoffResult { Acquired, ActivatedExisting, ExitTimedOut }

internal static class SingleInstanceHandoff
{
    internal static InstanceHandoffResult Acquire(Mutex mutex, EventWaitHandle closing, Action wake,
        int initialWaitMs = 150, int transitionWaitMs = 500, int exitWaitMs = 60_000)
    {
        if (WaitForOwnership(mutex, initialWaitMs)) return InstanceHandoffResult.Acquired;
        if (closing.WaitOne(0)) return WaitForClosingOwner(mutex, exitWaitMs);

        wake();
        // A wake can race the old instance entering shutdown. Observe that transition
        // instead of silently exiting while the old dispatcher ignores the request.
        int signal;
        try { signal = WaitHandle.WaitAny(new WaitHandle[] { mutex, closing }, transitionWaitMs); }
        catch (AbandonedMutexException ex) when (ex.MutexIndex == 0)
        {
            return InstanceHandoffResult.Acquired;
        }
        return signal switch
        {
            0 => InstanceHandoffResult.Acquired,
            1 => WaitForClosingOwner(mutex, exitWaitMs),
            _ => InstanceHandoffResult.ActivatedExisting,
        };
    }

    private static InstanceHandoffResult WaitForClosingOwner(Mutex mutex, int timeoutMs) =>
        WaitForOwnership(mutex, timeoutMs) ? InstanceHandoffResult.Acquired : InstanceHandoffResult.ExitTimedOut;

    private static bool WaitForOwnership(Mutex mutex, int timeoutMs)
    {
        try { return mutex.WaitOne(timeoutMs); }
        catch (AbandonedMutexException) { return true; }
    }
}
