#nullable enable
namespace VortexArena.Server.Core;

/// <summary>Shared drain step of every service's <c>StopAsync</c>: waits for the cancelled loops
/// before the caller closes their resources.
/// <para>⚠️ Closing a socket/timer while the loop is still inside <c>ReceiveAsync</c> /
/// <c>WaitForNextTickAsync</c> either raises an error or lets the loop print one more line AFTER
/// "Kapandı." — the drain is what keeps that last line last.</para></summary>
internal static class ServiceShutdown
{
    /// <summary>Per-service ceiling. Windows gives a console close handler ~5 s in total, so four
    /// services plus the control host must fit under it.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>Waits for the given loops, then observes their exceptions.</summary>
    /// <remarks>⚠️ ONE shared timeout for all loops (not one per task): per-task waits would add up
    /// and blow past the ceiling above.</remarks>
    public static async Task DrainAsync(string service, params Task?[] loops)
    {
        List<Task>? pending = null;
        foreach (var loop in loops)
        {
            if (loop is null || loop.IsCompleted) continue;
            (pending ??= new List<Task>()).Add(loop);
        }
        if (pending == null) return;

        var all = Task.WhenAll(pending);
        if (await Task.WhenAny(all, Task.Delay(Timeout)) != all)
        {
            Console.WriteLine($"[kapanış] {service} {Timeout.TotalSeconds:0} sn'de durmadı — zorla.");
            return;
        }

        // Awaiting the completed set is what OBSERVES the exceptions; without it a faulted loop
        // stays an unobserved task.
        try { await all; }
        catch (OperationCanceledException) { /* expected: this is how a cancelled loop ends */ }
        catch (Exception ex) { Console.WriteLine($"[kapanış] {service} hatayla bitti: {ex.Message}"); }
    }
}
