namespace SourceSharp.Abi;

/// <summary>
/// Declares a network string table — plan_gamedll.md Part 13,
/// plan_gamedll_after.md Phase A8, in the shape the owner asked for after it.
///
/// <para>
/// Goes on an <b>interface</b> that extends <see cref="IEngineStringTable"/>:
/// </para>
///
/// <code>
/// [StringTable("Example", 255)]
/// public interface ExampleStringTable : IEngineStringTable { }
/// </code>
///
/// <para>
/// <b>The declaration is the whole API.</b> A consumer injects
/// <c>ExampleStringTable</c> and calls <c>AddString</c> / <c>GetString</c> on
/// it. There is no <c>Create</c> to call, no <c>Find</c> to call and no
/// <c>TABLEID</c> anywhere — <see cref="StringTableRegistry"/> walks the
/// declarations, makes the table inside the one window the engine allows one
/// in, and registers an implementation under the declared interface.
/// </para>
///
/// <para>
/// <b>This is the house pattern, not a new one.</b> <c>[UserMessage("Fade",
/// Size = 10)]</c> and <c>[StandaloneNetState("classname")]</c> both declare by
/// attribute and are resolved by walking the loaded types, and
/// <see cref="UserMessageRegistry.RegisterAll"/> is the file this one was
/// written against.
/// </para>
///
/// <para>
/// <b>Where that house rule INVERTS, said out loud.</b>
/// <c>UserMessageRegistry</c> refuses an <c>IsAbstract || IsInterface</c>
/// declaration, because a user message must be a concrete named thing it can
/// instantiate. Here the declaration <i>is</i> an interface and a class is
/// refused — because what a consumer injects has to be a type it can name, and
/// the thing that gets instantiated is generated rather than written. The rule
/// behind both is the same: <b>the declaration is the identity, and the
/// identity is stable</b>. What differs is which side of the injection each
/// pattern's declaration sits on.
/// </para>
///
/// <para>
/// <b>ONE declaration, both sides, different behaviour by side</b> — the same
/// shape as <c>[StandaloneNetState]</c>. The owner's ruling: <b>the server only
/// CREATES, the client only FINDS, and the client may be LAZY.</b> There is no
/// second attribute and no find-only variant.
/// </para>
///
/// <para>
/// <b><see cref="MaxEntries"/> is required, and it is the server's business.</b>
/// The engine sizes a table when it creates it and there is no resize, so this
/// is the whole budget for the life of the map. The client never reads it: it
/// does not create, so it has nothing to size.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class StringTableAttribute : Attribute
{
    public StringTableAttribute(string name, int maxEntries)
    {
        Name = name;
        MaxEntries = maxEntries;
    }

    /// <summary>The table's name as the engine knows it.</summary>
    public string Name { get; }

    /// <summary>
    /// How many entries the table may hold. Fixed at creation — the engine
    /// sizes a table when it makes it and there is no resize.
    /// </summary>
    public int MaxEntries { get; }
}

/// <summary>
/// One network string table, as a mod holds it — plan_gamedll.md Part 13,
/// plan_gamedll_after.md Phase A8.
///
/// <para>
/// <b>Additive, and the word matters.</b> The seven tables the engine and the
/// rest of this binary already depend on — <c>Materials</c>,
/// <c>ParticleEffectNames</c>, <c>EffectDispatch</c>, <c>VguiScreen</c>,
/// <c>InfoPanel</c>, <c>Scenes</c>, <c>ServerMapCycle</c> — are created
/// natively in <c>shell_stringtables.cpp</c> and are <i>not</i> ported.
/// 141 native references read the globals behind them; every one is a
/// dereference that would still have to be answered whatever created the
/// table, so moving them buys nothing and risks the index-0 invariant below.
/// This is how a mod declares a table of its own, which is what
/// <see cref="IServerLevel.CreateNetworkStringTables"/> was reserved for.
/// </para>
///
/// <para>
/// <b>A table can only be CREATED from
/// <see cref="IServerLevel.CreateNetworkStringTables"/>.</b> The engine opens a
/// window for table creation there and refuses outside it, which is why that
/// slot exists at all — so that is where
/// <see cref="StringTableRegistry.CreateAll"/> runs, and it is the only place a
/// create happens. <b>There is no create on this interface at all</b>, so mod
/// code cannot ask for one from anywhere else and a client-side create is not
/// something that can be written.
/// </para>
///
/// <para>
/// <b>A HANDLE IS A NAME.</b> It does not carry the <c>TABLEID</c> it was made
/// with; every member below asks the engine which table has this name and then
/// does the one thing it was asked for. That is what lets the container hold
/// one instance for the life of the process while the engine remakes its tables
/// at every map load, and it is the answer to the failure an id has:
/// <b>a kept id addresses a POSITION, not a table.</b> The engine hands ids out
/// in creation order (<c>INetworkStringTable::GetTableId</c>) and resolves them
/// the same way (<c>INetworkStringTableContainer::GetTable</c>). Where the
/// population happens to match, a kept id lands on the right table and works —
/// which is exactly why the hazard survives testing; where a map, another mod
/// or a conditional create shifts it, every read and write goes silently to a
/// <i>different</i> table. <b>The native half cannot catch that case</b>:
/// <c>CShellStringTables::Resolve</c> warns for an id that resolves to nothing,
/// and an id that resolves to the wrong table is, from an id alone,
/// indistinguishable from a good one.
/// </para>
///
/// <para>
/// <b>So a handle never goes stale; it goes ABSENT, loudly.</b> When the name
/// is not there — a client whose table has not replicated yet, or a use before
/// this map's <c>CreateNetworkStringTables</c> has run — the operation is
/// refused with this area's own miss values (<c>-1</c>, or
/// <see langword="null"/> from <see cref="GetString"/>), the handle says so
/// once on the console naming the table, and <b>nothing crosses the
/// boundary</b>: no id is produced, so no other table can be touched. Nothing
/// is cached, so the very next call succeeds the moment the table is there —
/// which is what makes the client's lazy find correct rather than merely cheap.
/// </para>
///
/// <para>
/// <b>The index-0 invariant this must never break.</b> <c>debug/debugempty</c>
/// is index 0 of <c>Materials</c> and <c>error</c> is index 0 of
/// <c>ParticleEffectNames</c>, because <c>GetMaterialIndex</c> and
/// <c>GetParticleSystemIndex</c> both answer 0 for a name they cannot find — so
/// index 0 is what every <i>failed</i> lookup gets, and anything else there is
/// a real material silently standing in for the error one, everywhere, with no
/// diagnostic at all. An <see cref="AddString"/> to either table that ran
/// before those two precaches would do exactly that. The shell calls the
/// managed slot <i>after</i> the native one and nowhere else, and
/// <c>stringtable_index0_test.cpp</c> replays the whole shipped sequence — the
/// native function inlined, the managed call-out modelled as an add — so an
/// inversion fails on the replayed index rather than on a needle somebody had
/// to think of.
/// </para>
///
/// <para>
/// <b>Every miss is <c>-1</c></b>, and never the engine's
/// <c>INVALID_STRING_INDEX</c>: that constant is an unsigned short
/// <c>0xFFFF</c>, which would cross as 65535 — a plausible index that every
/// <c>if (i &gt;= 0)</c> reads as success.
/// </para>
///
/// <para>
/// <b>Server writes, client reads.</b> A client-side add to a server table is
/// the desync string tables exist to avoid, and it is refused in three places
/// rather than discouraged in one: the client fills neither the create nor the
/// add engine slot, <see cref="StringTableRegistry.CreateAll"/> is driven only
/// from the server's level slot, and <see cref="AddString"/> refuses on the
/// client before it reaches the boundary at all.
/// </para>
/// </summary>
public interface IEngineStringTable
{
    /// <summary>The table's name — the only thing a handle remembers.</summary>
    string Name { get; }

    /// <summary>
    /// The id the engine has for it <i>right now</i>, or <c>-1</c> when the
    /// table is not there. For a console line, and for asking whether the table
    /// has arrived; keeping it is the mistake this interface exists to remove.
    /// </summary>
    int Id { get; }

    /// <summary>
    /// How many strings the table holds. <c>-1</c> when there is no such table,
    /// which is a different answer from the <c>0</c> an empty one gives — a mod
    /// that asked for the wrong name would otherwise iterate nothing and
    /// conclude its own table is empty.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Adds a string and answers its index — the one it already had if the
    /// table held it, which is the engine's own behaviour rather than a special
    /// case. <c>-1</c> on refusal, including every add attempted on the client.
    /// </summary>
    int AddString(string value);

    /// <summary>The index of a string, or <c>-1</c> when the table does not hold it.</summary>
    int FindStringIndex(string value);

    /// <summary>
    /// One entry, by index. <see langword="null"/> when there is none — and
    /// null rather than empty on purpose: a table may legitimately hold an
    /// empty string, and collapsing the two makes "there is no such entry"
    /// indistinguishable from "the entry is blank".
    /// </summary>
    string? GetString(int stringIndex);
}

/// <summary>
/// The six id-addressed operations the ABI actually has.
///
/// <para>
/// <b>INTERNAL, and that is the change.</b> It is still an engine area — the
/// naming rule enrols it and <c>EngineStringTables</c> binds it — but nothing
/// outside this assembly can name it, so no mod code can be handed a
/// <c>TABLEID</c> to pass to whichever table holds that position today. The
/// public surface is <see cref="StringTableAttribute"/> and
/// <see cref="IEngineStringTable"/>, and neither of them has an id in it.
/// </para>
/// </summary>
internal interface IEngineStringTables
{
    /// <summary>
    /// Creates a table and answers its id, or the id of the table that already
    /// has that name — which is what a <c>cs_sv_reload</c> produces and is not
    /// an error. <c>-1</c> on refusal, including a create attempted outside
    /// <c>CreateNetworkStringTables</c>.
    /// </summary>
    int Create(string name, int maxEntries);

    /// <summary>The id of an existing table, or <c>-1</c>. Valid at any time.</summary>
    int Find(string name);

    int AddString(int tableId, string value);
    int FindStringIndex(int tableId, string value);
    string? GetString(int tableId, int stringIndex);
    int GetNumStrings(int tableId);
}

/// <summary>
/// The server's half of a string table: adding a string, by the id the table
/// has right now (P1 run 3: the add is a server-only slot, so the shared
/// handle holds one of these instead of naming the slot; the client holds
/// none and refuses). Implemented in Server.Interop (StringTableWriter).
/// </summary>
public interface IStringTableWriter
{
    /// <summary>The string's index, or <c>-1</c> on refusal.</summary>
    int AddString(int tableId, string value);
}

/// <summary>
/// The one implementation of <see cref="IEngineStringTable"/> — a name, the
/// area it asks, and a flag so an absent table is said once rather than
/// sixty-six times a second.
///
/// <para>
/// Every declared table's generated binding forwards to one of these, so the
/// re-find-by-name policy is written once and the engine double runs the same
/// one the game does.
/// </para>
/// </summary>
internal sealed class StringTableHandle : IEngineStringTable
{
    private readonly IEngineStringTables _tables;
    private readonly IStringTableWriter? _writer;
    private bool _saidItIsAbsent;

    // Where a write refused or a table absent is said (statics to zero).
    private readonly ILog _log;

    /// <summary>
    /// <paramref name="writer"/> is null on the client, and it is a
    /// constructor argument rather than a read of which side is executing for
    /// the same reason <c>UserMessageRegistry</c> takes <c>receives</c>: the
    /// side is known where the module is built, and a handle that asked a
    /// process-wide static would answer differently depending on which thread
    /// happened to call it.
    /// </summary>
    internal StringTableHandle(IEngineStringTables tables, string name, IStringTableWriter? writer, ILog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _tables = tables;
        _writer = writer;
        Name = name;
    }

    public string Name { get; }

    /// <summary>Asked for, never remembered. Silent: this is a probe, not a use.</summary>
    public int Id => _tables.Find(Name);

    public int Count => Resolved(out int tableId) ? _tables.GetNumStrings(tableId) : -1;

    /// <summary>
    /// <b>Refused on the client before the boundary.</b> The client fills
    /// neither the create nor the add engine slot, so an add there would answer
    /// <c>-1</c> anyway — silently, from a null slot. Refusing here makes it say
    /// which table and which side, which is the difference between a mod author
    /// finding this in a minute and finding it in an afternoon.
    /// </summary>
    public int AddString(string value)
    {
        if (_writer is null)
        {
            _log.Warning(
                $"[SourceSharp] string table '{Name}': the CLIENT cannot add to a string table. "
              + "The server writes these and the client reads them; a client-side add is the "
              + "desync string tables exist to avoid.\n");
            return -1;
        }

        return Resolved(out int tableId) ? _writer.AddString(tableId, value) : -1;
    }

    public int FindStringIndex(string value)
        => Resolved(out int tableId) ? _tables.FindStringIndex(tableId, value) : -1;

    public string? GetString(int stringIndex)
        => Resolved(out int tableId) ? _tables.GetString(tableId, stringIndex) : null;

    /// <summary>
    /// The name back to the id it has right now, or a refusal that says so.
    ///
    /// <para>
    /// The refusal is the whole staleness story: no id is produced, so nothing
    /// crosses and no other table can be reached. It is said ONCE per handle —
    /// loud enough to be found, quiet enough that a client reading a table that
    /// has not replicated yet does not bury the first line under sixty-six a
    /// second. <b>The ANSWER is not cached, only the line</b>: the very next
    /// call resolves again, and succeeds the moment the table is there.
    /// </para>
    /// </summary>
    private bool Resolved(out int tableId)
    {
        tableId = _tables.Find(Name);

        if (tableId >= 0)
            return true;

        if (!_saidItIsAbsent)
        {
            _saidItIsAbsent = true;
            _log.Warning(
                $"[SourceSharp] string table '{Name}' is not there; the operation was refused. "
              + "Tables are made again at every map load and reach the client by replication, so "
              + "this may simply be early — the next call asks again.\n");
        }

        return false;
    }
}
