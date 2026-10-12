using SourceSharp.Abi;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The CLIENT's entity directory — <c>plan_entity.md</c> §3.5, "one per side
/// module".
///
/// <para>
/// <b>WHAT THIS IS AND WHAT THE SLOT TABLE IS.</b> §3.3 splits the two and this
/// file is the managed half of that split. The native client slot table
/// (<c>slot_table.h</c>, answered to the engine as <c>IClientEntityList</c> by
/// <c>shell_cliententitylist.cpp</c>) has four operations — bind, unbind,
/// resolve-by-handle, resolve-by-index — and §3.3 says of it in as many words:
/// "It cannot iterate, has no listeners, and knows no names." Everything a list
/// actually <i>does</i> is here instead: "iterate, find by name, find by class,
/// notify on add and remove — lives in the managed <c>EntityDirectory</c>
/// (§3.5), one per side module."
/// </para>
///
/// <para>
/// So a question answered here must never grow a native implementation, and the
/// reverse holds too: this class never resolves an <i>engine</i> interface. It
/// knows managed client entities and the handles they are bound under, and
/// nothing else.
/// </para>
///
/// <para>
/// <b>THE STORAGE IS <see cref="EntityDirectoryCore{TEntity}"/> NOW, WHICH IS
/// THE FOLD <c>entlist-client</c> ASKED FOR.</b> That request said: "It is not
/// <c>SourceSharp.Abi/EntityDirectory</c> because that class binds
/// <c>ScriptedEntity</c>, which is the server's entity type; the client's is
/// <c>ClientStandaloneEntity</c> and the two share no base… The likely shape is
/// <c>EntityDirectory&lt;TEntity&gt;</c> or a shared non-generic core with the
/// entity array typed by the side." Both lanes merged with the ask unanswered;
/// lane <c>sub-entitylist</c> took it. The shape is the second one, generic:
/// the array by handle entry, the dense list with its inverse, the index table,
/// the classname and name indexes and the guarded detach all live once, in the
/// shared assembly, and this class is what is genuinely per side — the entity
/// type, the two counters and the two events.
/// </para>
///
/// <para>
/// <b>THE HANDLE IS COMPARED, NOT THE ENTRY.</b> A resolve that trusted the
/// entry alone would answer the new occupant for a dead entity's handle, which
/// is the one bug <c>CBaseHandle</c> exists to make impossible. Note that
/// <c>default(EntityHandle)</c> is <b>a valid handle</b> — entry 0, serial 0,
/// which on a live client is the worldspawn — so the empty slot is
/// <see cref="EntityHandle.None"/> (<c>uint.MaxValue</c>) and never
/// <c>default</c>. Two lanes of this port have already measured the wrong path
/// by assuming otherwise.
/// </para>
/// </summary>
public sealed class ClientEntityDirectory : EntityDirectoryCore<ClientStandaloneEntity>
{
    // NUM_ENT_ENTRIES is INHERITED and deliberately not restated here.
    // ClientEntityDirectory.EntryCount still resolves -- an inherited const is
    // reachable through the derived type name -- and there is exactly one
    // declaration of the size, which is what its own doc in the core asks for:
    // two spellings of one size is precisely the drift a fixed-width boundary
    // cannot see.

    /// <summary>
    /// The classname index's key — <c>ClientStandaloneEntity.ClassName</c>,
    /// which is the attribute the class was registered under.
    /// </summary>
    protected override string? ClassNameOf(ClientStandaloneEntity entity) => entity.ClassName;

    /// <summary>
    /// The engine index the shell cached at create. See
    /// <see cref="TracksEntityIndex"/>.
    /// </summary>
    protected override int EntityIndexOf(ClientStandaloneEntity entity) => entity.Index;

    /// <summary>
    /// TRUE, and it is the client half of the asymmetry the core describes: a
    /// client shell is told its entity index once, in its constructor, and
    /// caches it, so index → entity is a real table here and not the identity
    /// map it is on the server.
    /// </summary>
    protected override bool TracksEntityIndex => true;

    /// <summary>
    /// A handle must be BOUND before it can be named. The server's directory
    /// allows a name on an unbound handle — its map parse fills the name index
    /// from the <c>targetname</c> keyvalue — and the client has no such path:
    /// nothing sends a targetname over the wire, so a name on an unresolvable
    /// handle here could only be a mistake, and one that would sit in the index
    /// forever because nothing will ever unbind it.
    /// </summary>
    protected override bool CanName(EntityHandle handle) => Resolve(handle) is not null;

    /// <summary>
    /// Total binds and unbinds this directory has seen. Diagnostic only, and
    /// the pair is what makes a leak readable: <see cref="EntityDirectoryCore{TEntity}.Count"/>
    /// alone cannot tell "nothing was ever created" from "everything was
    /// created and removed".
    /// </summary>
    public long Binds { get; private set; }

    /// <summary>See <see cref="Binds"/>.</summary>
    public long Unbinds { get; private set; }

    //-------------------------------------------------------------------------
    // NOTIFY ON ADD AND REMOVE — §3.5's "notify", which is the managed answer
    // to the SDK's IEntityListener. The native side does not get one: gate 2
    // counts `IEntityListener` as an entity-list capability precisely because a
    // listener is the half of a list that leaves no member symbol behind.
    //-------------------------------------------------------------------------

    /// <summary>Raised after an entity is bound.</summary>
    public event Action<EntityHandle, ClientStandaloneEntity>? EntityBound;

    /// <summary>
    /// Raised after an entity is unbound, while the object is still alive.
    ///
    /// <para>
    /// AFTER the tables are cleared, not before: a listener that asks this
    /// directory a question during the notification must see the world the
    /// removal leaves, not the one it is halfway through leaving.
    /// </para>
    /// </summary>
    public event Action<EntityHandle, ClientStandaloneEntity>? EntityUnbound;

    //-------------------------------------------------------------------------
    // BIND / UNBIND.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Puts <paramref name="entity"/> in <paramref name="handle"/>'s entry and
    /// answers whether it took.
    ///
    /// <para>
    /// <paramref name="entityIndex"/> is the engine's entity index, or -1 when
    /// the caller has not got one. It is passed in rather than asked of the
    /// handle because asking costs an ABI crossing, and the shell writes it
    /// once in its constructor and never again.
    /// </para>
    ///
    /// <para>
    /// The refusals, the eviction and the indexing are the core's — see
    /// <see cref="EntityDirectoryCore{TEntity}.BindCore"/>. What is here is the
    /// counter and the notification, and they happen only when the bind took.
    /// </para>
    /// </summary>
    public bool Bind(EntityHandle handle, int entityIndex, ClientStandaloneEntity entity)
    {
        if (!BindCore(handle, entityIndex, entity))
            return false;

        Binds++;
        EntityBound?.Invoke(handle, entity);
        return true;
    }

    /// <summary>
    /// Empties <paramref name="handle"/>'s entry, IF that entry still holds
    /// this handle. Answers whether anything was there.
    /// </summary>
    public bool Unbind(EntityHandle handle)
    {
        if (!UnbindCore(handle, out ClientStandaloneEntity? leaving))
            return false;

        Unbinds++;

        if (leaving is not null)
            EntityUnbound?.Invoke(handle, leaving);

        return true;
    }

    //-------------------------------------------------------------------------
    // LEVEL CHANGE.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Drops every binding, notifying for each. A level change.
    ///
    /// <para>
    /// NOTIFIED RATHER THAN DROPPED SILENTLY, because a listener that took a
    /// reference on bind holds it until it is told, and a level change that
    /// said nothing would leak exactly the registrations a level change exists
    /// to clear. Answers how many were released.
    /// </para>
    ///
    /// <para>
    /// Deliberately NOT <see cref="EntityDirectoryCore{TEntity}.ClearCore"/>,
    /// which is the silent form: this side has listeners to tell, so it unbinds
    /// each entry instead.
    /// </para>
    /// </summary>
    public int Clear()
    {
        int released = 0;

        // OVER A COPY: Unbind edits the dense list, and the notification a
        // listener receives is allowed to touch the directory as well.
        foreach (int entry in DenseEntries.ToArray())
        {
            EntityHandle handle = HandleOf(entry);

            if (handle != EntityHandle.None && Unbind(handle))
                released++;
        }

        return released;
    }
}
