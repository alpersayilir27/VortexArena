using System.Diagnostics;
using System.Threading;

namespace VortexArena.Launcher.Infrastructure;

/// <summary>
/// Keeps one launcher per session. The second launch signals a named event and exits; the owner
/// wakes up and brings its window forward.
/// <para>⚠️ Names are session-local (no <c>Global\</c>): a global object needs a privilege the
/// operator account may not have, and two Windows sessions are two separate operators anyway.</para>
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "VortexArena.Launcher.Instance";
    private const string SignalName = "VortexArena.Launcher.Activate";

    private Mutex? _mutex;
    private EventWaitHandle? _signal;
    private RegisteredWaitHandle? _registration;

    public bool IsOwner { get; private set; }

    /// <summary>True when this process owns the single slot; false means another launcher is up.</summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: false, MutexName, out _);
        try
        {
            IsOwner = _mutex.WaitOne(TimeSpan.Zero, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            // Previous owner crashed; the slot is ours.
            IsOwner = true;
        }

        return IsOwner;
    }

    /// <summary>Owner side: runs <paramref name="onActivate"/> whenever another launch signals.</summary>
    public void ListenForActivation(Action onActivate)
    {
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _signal, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Second-launch side: wakes the owner. False when no owner is listening.</summary>
    public static bool SignalExistingInstance()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(SignalName);
            signal.Set();
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Exe path of the launcher already running; null when it cannot be read.</summary>
    public static string? RunningInstancePath()
    {
        using var self = Process.GetCurrentProcess();
        var processes = Process.GetProcessesByName(self.ProcessName);
        try
        {
            foreach (var process in processes)
            {
                if (process.Id == self.Id) continue;
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path)) return path;
                }
                catch (Exception)
                {
                    // Elevated or exiting process: path unreadable.
                }
            }

            return null;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _registration = null;
        _signal?.Dispose();
        _signal = null;

        if (_mutex is not null)
        {
            if (IsOwner)
            {
                try { _mutex.ReleaseMutex(); }
                catch (ApplicationException) { /* never owned */ }
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }
}
