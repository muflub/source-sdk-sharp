namespace SourceSharp.Abi;

/// <summary>
/// Where an entity's state waits while the assembly that produced it is
/// unloaded and its replacement is loaded.
///
/// <para>
/// IT HAS TO BE HERE AND NOWHERE ELSE. The ABI lives in the side's ROOT
/// context, kept for the process (plan_side_isolation P5); every other candidate — the module, the registry, a static on an
/// entity class — lives in the context that is about to cease to exist, so
/// anything left there would go with it. That is the whole reason this is a
/// separate class rather than a field on the registry that fills it.
/// </para>
///
/// <para>
/// <b>IT HOLDS BYTES, AND ONLY BYTES</b> (<c>plan_abi_hotreload</c> D6). A
/// dictionary in the default context holding one object of a side keeps that
/// side's collectible context alive forever — the leak <c>ManagedCaches</c>
/// exists to prevent, whose symptom is not an error but a reload that quietly
/// keeps running the code it was supposed to replace. The records it used to
/// hold were made only of strings and numbers for that reason, so they pinned
/// no side; but they pinned their own TYPES here, and through them the
/// declared-variable, entity-directory and shell-less code. Each section is now
/// what <c>SavedEntityCodec</c> wrote for it, and <c>ReloadCarrier</c> is the
/// typed door in front of this class. This class names no record type.
/// </para>
///
/// <para>
/// PER SIDE, because a listen server runs two of these in one process and
/// <c>cs_sv_reload</c> reloads one of them. A single store would have had the
/// server's save waiting for a client restore that is not coming.
/// </para>
///
/// <para>
/// TAKING EMPTIES IT, deliberately. A restore that ran twice would replay every
/// map key a second time and re-stamp every output tally; and state left behind
/// after a restore would be applied to whatever entity took that handle next,
/// which is a different entity wearing the old one's values. One save, one
/// restore, nothing kept.
/// </para>
///
/// <para>
/// <b>AN INSTANCE OF THE SIDE'S STATE</b> (statics to zero). It is the
/// hand-over between a side's old collectible context and its new one, so it
/// is owned by something that outlives both: the side's <see cref="SideState"/>,
/// which the Bootstrap keeps for the side root's life and hands to every load.
/// Two game instances of a side are two states and so two stores.
/// </para>
/// </summary>
public sealed class ReloadStore
{
    /// <summary>
    /// One carried section: the bytes, and how many records they hold, so a
    /// readout can say what is waiting without decoding it.
    /// </summary>
    public readonly record struct Carried(byte[] Bytes, int Count);

    private readonly Dictionary<string, Carried> _sections = new(StringComparer.Ordinal);
    private ReloadTally _saved;
    private ReloadTally _restored;
    private int _restoreFailures;
    private string? _restoreFirstFailure;

    public ReloadStore(Side side) : this(side, null) { }

    /// <param name="side">The side whose reloads this store carries.</param>
    /// <param name="owner">
    /// The side state that keeps it, whose console a refusal to carry is said
    /// on (statics to zero); a store built alone says it on standard error.
    /// </param>
    public ReloadStore(Side side, SideState? owner)
    {
        Side = side;
        _owner = owner;
    }

    private readonly SideState? _owner;

    /// <summary>Where a section that cannot be carried is said: the side's current console.</summary>
    public ILog Log => _owner?.Log ?? new StandardErrorLog();

    /// <summary>The side whose reloads this store carries, for what it logs.</summary>
    public Side Side { get; }

    /// <summary>
    /// Leaves <paramref name="bytes"/> in <paramref name="section"/> for the
    /// next load. Empty bytes or no records drop the section instead.
    /// </summary>
    public void Put(string section, byte[]? bytes, int count)
    {
        lock (_sections)
        {
            if (bytes is null || bytes.Length == 0 || count <= 0)
                _sections.Remove(section);
            else
                _sections[section] = new Carried(bytes, count);
        }
    }

    /// <summary>Hands over a section and forgets it; null when nothing waits.</summary>
    public byte[]? Take(string section)
    {
        lock (_sections)
        {
            if (!_sections.Remove(section, out var carried))
                return null;

            return carried.Bytes;
        }
    }

    /// <summary>A section's bytes without taking them, for a merge or a readout.</summary>
    public byte[]? Peek(string section)
    {
        lock (_sections)
            return _sections.TryGetValue(section, out var carried) ? carried.Bytes : null;
    }

    /// <summary>How many records a section holds; 0 when none waits.</summary>
    public int Count(string section)
    {
        lock (_sections)
            return _sections.TryGetValue(section, out var carried) ? carried.Count : 0;
    }

    /// <summary>Every section waiting, by name, with its byte size and record count: a readout.</summary>
    public IReadOnlyList<(string Section, int Bytes, int Count)> Sections()
    {
        lock (_sections)
            return _sections.OrderBy(p => p.Key, StringComparer.Ordinal)
                            .Select(p => (p.Key, p.Value.Bytes.Length, p.Value.Count))
                            .ToArray();
    }

    /// <summary>Drops every section waiting.</summary>
    public void Clear()
    {
        lock (_sections)
            _sections.Clear();
    }

    /// <summary>
    /// What one save or restore carried, measured by ONE method
    /// (<c>SavedEntityCodec.Measure</c>) so a save and its restore are counted
    /// alike and can be compared.
    /// </summary>
    public readonly record struct ReloadTally(
        int Entities, int Classes, int Fields, int Bytes,
        int Parts, int PartFields, int PartBytes,
        int IoEvents, int IoConnections, int ShellLess);

    /// <summary>Records what the last save carried.</summary>
    public void NoteSaved(ReloadTally tally)
    {
        lock (_sections)
            _saved = tally;
    }

    /// <summary>Records what the last restore put back.</summary>
    public void NoteRestored(ReloadTally tally)
    {
        lock (_sections)
            _restored = tally;
    }

    /// <summary>What the last save carried.</summary>
    public ReloadTally Saved
    {
        get
        {
            lock (_sections)
                return _saved;
        }
    }

    /// <summary>What the last restore put back.</summary>
    public ReloadTally Restored
    {
        get
        {
            lock (_sections)
                return _restored;
        }
    }

    /// <summary>How many records the last restore refused, and the first refusal.</summary>
    public (int Count, string? First) RestoreFailures
    {
        get
        {
            lock (_sections)
                return (_restoreFailures, _restoreFirstFailure);
        }
    }

    /// <summary>
    /// How many cached types the ABI forgot at the last unload of this side
    /// (ManagedCaches.Forget): the reload's leak arm reads it. -1 before any
    /// unload, which is the reading that means Forget was never reached. Zero is
    /// the ordinary answer since the type caches became each composition's own:
    /// Forget's only entries are the server registry's routes, which its
    /// Shutdown detaches first (live-gate's client_reload_caches_verdict).
    /// </summary>
    public int CachesDropped
    {
        get
        {
            lock (_sections)
                return _cachesDropped;
        }
    }

    /// <summary>Records what the last unload's ManagedCaches.Forget dropped.</summary>
    public void NoteCachesDropped(int count)
    {
        lock (_sections)
            _cachesDropped = count;
    }

    private int _cachesDropped = -1;

    /// <summary>Records the last restore's refusals.</summary>
    public void NoteRestoreFailures(int count, string? first)
    {
        lock (_sections)
        {
            _restoreFailures = count;
            _restoreFirstFailure = first;
        }
    }
}
