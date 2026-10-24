using RippersCasino.Server.Tables;
using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Models.Common;

namespace RippersCasino.Server.Transactions;

/// <summary>
/// Serializes casino operations with the game's normal item-event mutations.
/// Background services and other mods that bypass item events are outside this gate.
/// </summary>
public static class ProfileGate
{
    public static ConcurrentDictionary<MongoId, SemaphoreSlim> Gates { get; } = new();

    public static SemaphoreSlim For(MongoId sessionId) => Gates.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

    public static void Patch(Harmony harmony)
    {
        var original = typeof(ItemEventCallbacks).GetMethod(nameof(ItemEventCallbacks.HandleEvents), BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMethodException("SPT item-event callback was not found.");
        if (original.ReturnType != typeof(ValueTask<string>))
            throw new NotSupportedException("Casino requires the SPT 4.1.6 item-event callback signature.");

        harmony.Patch(original,
            prefix: new HarmonyMethod(typeof(ProfileGate), nameof(Before)),
            postfix: new HarmonyMethod(typeof(ProfileGate), nameof(After)),
            finalizer: new HarmonyMethod(typeof(ProfileGate), nameof(OnException)));
    }

    private static void Before(MongoId sessionID, out Lease __state)
    {
        CasinoTableService.Current?.RecoverProfileAsync(sessionID).GetAwaiter().GetResult();
        __state = new Lease(For(sessionID));
        try
        {
            // Harmony must acquire before invoking the async original; a postfix
            // alone would let its synchronous mutation prefix run concurrently.
            __state.Acquire();
            CasinoTableService.Current?.RequireDurable(sessionID);
            CasinoPersistence.Current?.EnsureDurableAsync(sessionID).GetAwaiter().GetResult();
        }
        catch
        {
            __state.Release();
            throw;
        }
    }

    private static void After(ref ValueTask<string> __result, Lease __state)
        => __result = ReleaseAfterAsync(__result, __state);

    private static Exception? OnException(Exception? __exception, Lease? __state)
    {
        if (__exception is not null)
            __state?.Release();
        return __exception;
    }

    private static async ValueTask<string> ReleaseAfterAsync(ValueTask<string> operation, Lease lease)
    {
        try
        {
            return await operation.ConfigureAwait(false);
        }
        finally
        {
            lease.Release();
        }
    }

    private sealed class Lease(SemaphoreSlim gate)
    {
        private int acquired;

        public void Acquire()
        {
            gate.Wait();
            Volatile.Write(ref acquired, 1);
        }

        public void Release()
        {
            if (Interlocked.Exchange(ref acquired, 0) == 1)
                gate.Release();
        }
    }
}
