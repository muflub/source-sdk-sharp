using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// The client's record of its own reloads (plan_client_novalve §3.8), carried
/// from the build that saved to the build that restored, in bytes like
/// everything else: the sequence, the sections the old build left, its
/// participant and entity counts, and a WEAK handle to its load context, which
/// is the leak arm. The new build checks that handle: dead (within a bounded
/// number of collections) is the reload working; alive is the old build
/// pinned by something, which every later reload would leak again.
/// </summary>
public sealed class ClientReloadLedger(ReloadStore store, IGameClock clock, ILog log)
{
    /// <summary>The ledger's own reload section.</summary>
    public const string Section = "client.reload-ledger";

    /// <summary>The most collections a restore runs while waiting for the old context to go.</summary>
    public const int MaxCollections = 10;

    /// <summary>One reload, as the restoring build saw it.</summary>
    public sealed record Entry(
        int Sequence, float SavedAt, float RestoredAt, IReadOnlyList<(string Name, int Bytes, int Count)> Carried,
        int ParticipantsSaved, int EntitiesSaved, ReloadParticipantTally? Restored, int EntitiesRestored,
        int CachesDropped, string OldContext, int Collections, double WallMilliseconds = 0);

    /// <summary>The last reload, or null before the first.</summary>
    public Entry? Last { get; private set; }

    /// <summary>Reloads this process has made of the client.</summary>
    public int Sequence => Last?.Sequence ?? 0;

    private (int Sequence, float SavedAt, nint Handle, int Participants, int Entities,
             List<(string, int, int)> Carried, long SavedStamp)? _incoming;

    /// <summary>The old build's half, last in its ReloadSave: what it is leaving, and a weak handle to itself.</summary>
    public void Save(int participantsSaved, int entitiesSaved)
        => Save(participantsSaved, entitiesSaved, AssemblyLoadContext.GetLoadContext(typeof(ClientReloadLedger).Assembly)
                                                  ?? AssemblyLoadContext.Default);

    /// <summary><see cref="Save(int,int)"/> with the object whose liveness the next build checks: a fact's seam.</summary>
    internal void Save(int participantsSaved, int entitiesSaved, object subject)
    {
        var carried = store.Sections();
        var handle = GCHandle.Alloc(subject, GCHandleType.WeakTrackResurrection);

        var w = new ReloadWriter();
        w.Write(Sequence + 1);
        w.Write(clock.Now);
        long pointer = GCHandle.ToIntPtr(handle);
        w.Write((int)(pointer & 0xFFFFFFFF));
        w.Write((int)(pointer >> 32));
        w.Write(participantsSaved);
        w.Write(entitiesSaved);
        w.Write(carried.Count);
        foreach (var (name, bytes, count) in carried)
        {
            w.Write(name);
            w.Write(bytes);
            w.Write(count);
        }
        // Last, and read only when present: the wall clock at the save, so the
        // restore can say how long the swap took (the game clock stands still
        // across it). Stopwatch is per process, which a reload stays in.
        long stamp = Stopwatch.GetTimestamp();
        w.Write((int)(stamp & 0xFFFFFFFF));
        w.Write((int)(stamp >> 32));

        store.Put(Section, w.ToArray(), 1);
    }

    /// <summary>The new build's first step in its ReloadRestore: take what the old one left. False on a fresh load.</summary>
    public bool BeginRestore()
    {
        _incoming = null;
        if (store.Take(Section) is not { } bytes)
            return false;

        try
        {
            var r = new ReloadReader(bytes);
            int sequence = r.ReadInt();
            float savedAt = r.ReadFloat();
            long pointer = (uint)r.ReadInt() | ((long)r.ReadInt() << 32);
            int participants = r.ReadInt();
            int entities = r.ReadInt();
            int n = r.ReadInt();
            var carried = new List<(string, int, int)>(Math.Clamp(n, 0, 1024));
            for (int i = 0; i < n; i++)
                carried.Add((r.ReadString(), r.ReadInt(), r.ReadInt()));
            long stamp = r.AtEnd ? 0 : (uint)r.ReadInt() | ((long)r.ReadInt() << 32);

            _incoming = (sequence, savedAt, (nint)pointer, participants, entities, carried, stamp);
            return true;
        }
        catch (ReloadFormatException e)
        {
            log.Warning($"[SourceSharp/Client] reload: the ledger the old build left would not read ({e.Message})\n");
            return false;
        }
    }

    /// <summary>The new build's last step: the restore's counts, the caches the ABI dropped, and the leak arm.</summary>
    public Entry? FinishRestore(ReloadParticipantTally? restored, int entitiesRestored)
    {
        if (_incoming is not { } incoming)
            return null;
        _incoming = null;

        double wallMs = incoming.SavedStamp == 0 ? 0 : Stopwatch.GetElapsedTime(incoming.SavedStamp).TotalMilliseconds;
        var (state, collections) = AwaitCollected(incoming.Handle);
        if (state == "alive")
            log.Warning($"[SourceSharp/Client] reload {incoming.Sequence}: the old build's load context is still " +
                        $"alive after {collections} collection(s); every reload will leak it\n");

        Last = new Entry(incoming.Sequence, incoming.SavedAt, clock.Now, incoming.Carried,
                         incoming.Participants, incoming.Entities, restored, entitiesRestored,
                         store.CachesDropped, state, collections, wallMs);
        return Last;
    }

    /// <summary>
    /// Waits, a bounded number of full collections, for a weak handle's target
    /// to go, and frees the handle. "unknown" when there was no handle.
    /// </summary>
    internal static (string State, int Collections) AwaitCollected(nint pointer)
    {
        if (pointer == 0)
            return ("unknown", 0);

        var handle = GCHandle.FromIntPtr(pointer);
        try
        {
            int collections = 0;
            while (IsAlive(handle) && collections < MaxCollections)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                collections++;
            }

            return (IsAlive(handle) ? "alive" : "collected", collections);
        }
        finally
        {
            handle.Free();
        }
    }

    // Not inlined, so no reference to the target outlives the check in this frame.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive(GCHandle handle) => handle.Target is not null;
}

/// <summary>Queues the client's managed reload through the engine (cs_cl_reload's fixed text).</summary>
public sealed class ClientReloadRequester(IFwdEngineClient engine)
{
    /// <summary>Whether a request was accepted by this build; the reload replaces the build that answers.</summary>
    public bool Pending { get; private set; }

    /// <summary>Asks the engine client to reload at its next frame boundary.</summary>
    public void Request()
    {
        engine.RequestManagedReload();
        Pending = true;
    }
}
