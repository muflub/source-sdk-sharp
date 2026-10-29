using SourceSharp.Abi;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// Names a class as the CLIENT half of a standalone entity.
///
/// The classname is the same one the server uses and the same one the map
/// writes — it is networked precisely so this side can pick a class by it.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ClientStandaloneEntityAttribute : Attribute
{
    public ClientStandaloneEntityAttribute(string className) => ClassName = className;
    public string ClassName { get; }
}

/// <summary>
/// The client's half of an entity, and the mirror of the server's
/// <c>StandaloneEntity</c>.
///
/// <para>
/// It is a SEPARATE class rather than the same one, which is what Source itself
/// does — <c>C_BaseEntity</c> and <c>CBaseEntity</c> are two types, not one
/// compiled twice. The two halves want different things: the server decides and
/// the client shows, and a single class would carry both sets of members onto
/// both sides and invite each to touch state it has no business in.
/// </para>
///
/// <para>
/// What arrives here is only what was networked. There are no keyvalues, no
/// inputs and no thinks — the server has all of those — so this side's job is
/// presentation, and it starts with knowing what it is and what it looks like.
/// </para>
/// </summary>
public abstract class ClientStandaloneEntity
{
    public ClientStandaloneEntity(IEngineEntityRender render)
    {
        Render = render;
    }

    /// <summary>what this entity looks like.</summary>
    protected readonly IEngineEntityRender Render;


    public EntityHandle Handle { get; internal set; }

    /// <summary>
    /// This entity's log: its registry's, over the client's console (statics
    /// to zero: it was the static Log), set when the registry creates it. An
    /// entity built on its own prints to standard error.
    /// </summary>
    protected internal ILog Log
    {
        get => _log ??= new StandardErrorLog();
        internal set => _log = value;
    }

    private ILog? _log;

    /// <summary>
    /// The engine's entity index for this entity, read ONCE at the creation
    /// push and never asked for again.
    ///
    /// <para>
    /// A MIRROR OF AN IMMUTABLE, and the write-site census is what licenses it:
    /// the shell's <c>m_nEntIndex</c> has exactly one writer in the whole tree —
    /// the constructor's initialiser list at
    /// <c>shell_standalone_client.cpp:119</c>, from the entity number the engine
    /// handed it. Every other mention of the member is a read
    /// (<c>:352</c>, <c>:375</c>, <c>:396</c>, and <c>entindex()</c> at
    /// <c>shell_standalone_client.h:231</c>, which is what the
    /// <c>EntityGetIndex</c> slot returns). A value with one writer that runs
    /// once cannot go stale.
    /// </para>
    ///
    /// <para>
    /// WHY IT IS WORTH MIRRORING: <c>light_dynamic</c> and <c>spotlight_end</c>
    /// key their pool slot by it, once per entity per frame, which measured
    /// 9.000 crossings per client frame on <c>dm_lockdown</c> — the whole of the
    /// client's <c>EntityGetIndex</c> traffic, spent re-reading a constant.
    /// </para>
    ///
    /// <para>
    /// −1 BEFORE THE REGISTRY FILLS IT AND AFTER IT IS REMOVED, which is the
    /// same answer the slot itself gives for a handle this side cannot resolve
    /// (<c>SourceSharp_ClientEntityGetIndex</c> returns −1 rather than masking
    /// the index out of the handle, precisely so a handle naming something else
    /// is not answered with an index it cannot honour). Cleared on removal
    /// because indices are RECYCLED: an entity that kept its old number would
    /// key a pool slot another entity now owns.
    /// </para>
    /// </summary>
    public int Index { get; private set; } = -1;

    /// <summary>
    /// Handed over by the registry, at the creation push and again at removal.
    /// See <see cref="Index"/>.
    /// </summary>
    internal void BindIndex(int index) => Index = index;

    //-------------------------------------------------------------------------
    // Hot reload. The server's ScriptedEntity carries the same pair and carries
    // more, because it has map keys and outputs to carry; this side has neither.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Writes this entity's state into a form that survives a client reload.
    ///
    /// <para>
    /// LESS TO CARRY THAN THE SERVER HAS, and the difference is worth stating.
    /// There are no keyvalues here and no outputs; what a client entity holds is
    /// what was networked to it, and one might expect that to need no carrying
    /// at all since the server will send it again. It will not: an entity whose
    /// state has not changed is not transmitted, so an entity that went quiet
    /// before the reload would sit on a buffer of zeroes until something moved
    /// it — a sprite that stopped drawing, with no error and no obvious cause.
    /// </para>
    ///
    /// <para>
    /// The same rule as the server's: an override calls
    /// <c>base.SaveState(state)</c>, and one that does not is named by the
    /// registry and has the default run for it.
    /// </para>
    /// </summary>
    public virtual void SaveState(SavedEntity state) => SaveDeclaredState(state);

    /// <summary>
    /// The discoverable half, reachable without going through the virtual:
    /// all three tiers, as the server's base walks them
    /// (plan_client_novalve §3.8). It carried the <c>[NetState]</c> objects
    /// only, so a <c>[SaveField]</c> or <see cref="ISaveCustom"/> on a client
    /// class was silently dropped.
    /// </summary>
    internal void SaveDeclaredState(SavedEntity state)
    {
        state.DefaultRan = true;

        foreach (var netState in NetStates)
            NetStateTransfer.Save(netState, state);

        SaveTiers.SaveMarked(this, state, Subject, Log);
        SaveTiers.SaveCustom(this, state, Subject, Log);
    }

    /// <summary>What a save or restore complaint names: the class and the handle's entry.</summary>
    private string Subject => $"{ClassName} #{Handle.EntryIndex}";

    /// <summary>Puts back what <see cref="SaveState"/> carried across.</summary>
    public virtual void LoadState(SavedEntity state) => LoadDeclaredState(state);

    /// <summary>The discoverable half of the restore.</summary>
    internal void LoadDeclaredState(SavedEntity state)
    {
        state.DefaultRan = true;

        foreach (var netState in NetStates)
            NetStateTransfer.Load(netState, state, ClassName, state.Name, Log);

        var handles = new SavedHandles(Host, Entities);
        SaveTiers.LoadMarked(this, state, Subject, handles, Log);
        SaveTiers.LoadCustom(this, state, Subject, handles, Log);
    }

    /// <summary>
    /// The engine, for the calls this entity makes about ITSELF.
    ///
    /// The client's counterpart to <see cref="ScriptedEntity.Entities"/>, and
    /// registry-assigned for the same reason: its subject is always this
    /// entity, which puts it with <see cref="Handle"/> rather than with the
    /// dependencies a class declares in its constructor.
    ///
    /// <para>
    /// Unset until the registry binds it (plan_abi_hotreload D8): there is no
    /// engine to default to, since the active side's defaults were a
    /// process-wide route and an entity's engine is its own side's. Every
    /// entity the game makes comes out of the registry, which binds it before
    /// anything can call the entity.
    /// </para>
    /// </summary>
    protected IEngineEntities Entities { get; private set; } = null!;

    /// <summary>Handed over by the registry. See <see cref="Entities"/>.</summary>
    internal void BindEngine(IEngineEntities entities, IEngineHost host)
    {
        Entities = entities;
        Host = host;
    }

    /// <summary>The side's engine host, for the handle re-check a restore makes. Bound with <see cref="Entities"/>.</summary>
    private IEngineHost Host { get; set; } = null!;

    /// <summary>
    /// The [NetState] objects on this entity, found once when it was created.
    /// Kept so an update does not have to reflect to find them again — this runs
    /// per entity per change.
    /// </summary>
    internal NetState[] NetStates = Array.Empty<NetState>();

    /// <summary>
    /// Decodes the entity's own replicated state and then tells the class.
    ///
    /// The decode happens FIRST so that OnDataChanged reads properties rather
    /// than bytes — which is the whole point of declaring a state object, and
    /// the difference between an entity class and a parser.
    /// </summary>
    internal void DispatchDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        foreach (var state in NetStates)
            state?.Decode(blob);

        OnDataChanged(blob, updateType);
    }

    public abstract string ClassName { get; }

    /// <summary>Created, and its first networked state has arrived.</summary>
    public virtual void OnSpawn() { }

    /// <summary>Removed on the server, or left the client's PVS for good.</summary>
    public virtual void OnRemoved() { }

    /// <summary>
    /// Draw this entity, once per frame.
    ///
    /// <para>
    /// Only called for an entity that asked, through
    /// <see cref="IEngineEntityRender.SetCustomDraw"/>. Most do not: an entity with
    /// a model is drawn by the shell and has nothing to add here.
    /// </para>
    ///
    /// <para>
    /// This is on the render path. It runs per entity per frame, so it should
    /// hand values to the engine and do no work of its own.
    /// </para>
    /// </summary>
    /// <returns>1 if anything was drawn, which is what DrawModel answers.</returns>
    public virtual int OnDraw() => 0;

    /// <summary>
    /// This entity's draw prepared ahead of its DrawModel, in the view's one
    /// batch (C7 item 7): what DrawModel will answer once native carries the
    /// prepared draw out, -1 when it must draw in place through
    /// <see cref="OnDraw"/> (the default), or <see cref="DrawsInPlace"/> for an
    /// entity whose draw has no prepared form.
    /// </summary>
    public virtual int OnDrawBatched() => -1;

    /// <summary>
    /// <see cref="OnDrawBatched"/>'s answer for a draw with no prepared form: it
    /// is made at the entity's DrawModel, so native counts it as drawn in place,
    /// not as a batch that fell back.
    /// </summary>
    public const int DrawsInPlace = -2;

    /// <summary>
    /// What this entity draws beside its model in one pass, with the pass's
    /// <c>STUDIO_*</c> flags (<see cref="Render.ViewDrawProgram.StudioRender"/> and its neighbours).
    ///
    /// <para>
    /// Asked at two places, both stock's. After the LOCAL VIEWMODEL's model,
    /// in the viewmodel pass: <c>C_BaseViewModel::DrawModel</c>'s call to the
    /// weapon's <c>ViewModelDrawn</c>, where the RPG's glow and the
    /// physcannon's and stunstick's sprites are drawn. And in the TRANSLUCENT
    /// pass of an entity in the two-pass group, before its model: the
    /// stunstick's and physcannon's <c>DrawModel</c> arm for
    /// <c>STUDIO_TRANSPARENCY</c>, which draws the effects and answers 1
    /// without drawing the model again.
    /// </para>
    /// </summary>
    /// <returns>
    /// What DrawModel answers in place of the model's own draw in this pass,
    /// or -1 to let the model draw (the default). Ignored after the viewmodel,
    /// whose model has already drawn.
    /// </returns>
    public virtual int OnDrawExtras(int flags) => -1;

    /// <summary>
    /// The class's own replicated state, freshly unpacked.
    ///
    /// <para>
    /// Only classes that declared a <see cref="SourceSharp.Shared.StandaloneNetStateAttribute"/>
    /// have anything here, and they are the minority on purpose: an entity that
    /// networks nothing beyond origin, model, classname and angles pays for no
    /// table of its own.
    /// </para>
    ///
    /// <para>
    /// The span is BORROWED native memory and is valid only for this call, so a
    /// value worth keeping must be copied out. It is handed over on every
    /// update and not only the first, because a value that changes is the whole
    /// reason a class declares one.
    /// </para>
    /// </summary>
    public virtual void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType) { }
}
