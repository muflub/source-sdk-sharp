namespace SourceSharp.Abi;

//=============================================================================
// THE FIRST AREA TO LEAVE THE FLAT TABLE — plan_native_api.md Phase 2.
//
// EngineFrame used to be seventeen one-line forwards into Engine.cs, each of
// which was a call through one of seventeen ABI fields, each of which was
// spelled three times by hand: the field in sourcesharp.h, the mirror in
// Abi.cs, and the assignment in the gamesystem fill. It is now seventeen
// one-line calls through one vtable, and the seventeen fields are orphans.
//
// WHY THIS AREA WENT FIRST, and it was chosen on the triage rather than on
// preference: seventeen fields, all server-only, every signature scalar, every
// body already in one file (shell_frame.cpp), no engine interface
// entangled and no dual-side split to decide. And it is the most VERIFIABLE
// area in the tree -- these methods run sixty-six times a second, so
// `make live-gate`'s two map loads exercise every one of them, including the
// reload path, within seconds. If the recipe survives contact with the frame
// loop it survives anywhere.
//
//-----------------------------------------------------------------------------
// GAME CODE DID NOT CHANGE AND WAS NOT MEANT TO. IEngineFrame is what game code
// holds and its members are what they were; this class is the half behind it.
// That is the unit of migration -- the area, not the entry.
//
//-----------------------------------------------------------------------------
// BINDING IS LAZY, AND ON THIS AREA THAT IS NOT AN OPTIMISATION.
//
// Areas are registered on BOTH sides (EngineServices.Bindings reflects over
// every IEngine* interface and constructs its Engine* class), but
// SourceSharpFrame001 is served only by the server: the client has no
// GameFrame to have steps of. Acquiring in the constructor would therefore
// throw while the client's container was being built, and an area that is
// never called failing to construct is a worse failure than a call that has
// nowhere to go.
//
// So the acquire happens at the first CALL, and it CREATES rather than
// TRY-CREATES: an area that cannot reach its interface is not a degraded area,
// it is a bug, and the exception names the factory kind, the version string and
// native's own reason at the site that asked. On the client no caller exists,
// so it never runs; if one appears it says so instead of doing nothing, which
// is what the flat slots did (a null field, a silent return, and a frame step
// that quietly did not happen).
//
//-----------------------------------------------------------------------------
// TWO MEMBERS DO NOT REACH THE VTABLE FIRST, AND THAT IS PRESERVED BEHAVIOUR
// RATHER THAN AN EXCEPTION TO THE RECIPE.
//
// plan_crossings.md Phase X1 bound the engine's own CGlobalVars, and
// TickCount and GetFrameTime have read `gpGlobals->tickcount` and
// `->frametime` through it ever since, falling back to a crossing only when
// the pointer is not bound. Rerouting them straight at the vtable would have
// undone that -- 11 crossings a server frame back, measured in
// docs/a11-frame-crossing-census.tsv -- and would ALSO have changed what
// tools/gate5 records: the native step functions call ShellGameDll_RecordStep,
// so a read that used to be a Globals load would start appearing in the step
// ring. The transport moved; the decision above it did not.
//=============================================================================

/// <summary>
/// <see cref="IEngineFrame"/> over the shim layer's own <c>IShimFrame</c>,
/// called through its forwarders (<c>SSFwd_ShimFrame</c>,
/// plan_forwarders_everywhere.md): no slot of the object's vtable is read here.
/// </summary>
internal sealed unsafe class EngineFrame : IEngineFrame
{
    /// <summary>
    /// SourceSharp's own factory name for the area, in the engine's own
    /// convention — <c>SourceSharp&lt;Area&gt;001</c>, with no
    /// <c>Client</c>/<c>Server</c> suffix because this area does not split.
    /// </summary>
    public const string InterfaceVersion = "SourceSharpFrame001";

    private readonly NativeInterfaceFactory _factory;

    // The side whose CGlobalVars TickCount and GetFrameTime read first
    // (plan_abi_hotreload D8: this binding's own, not the active side's).
    private readonly EngineSide _side;

    private void* _self;
    private ShimFrameFwd* _vtbl;

    /// <summary>
    /// Over <paramref name="side"/> and a factory: the side's own when the
    /// container builds it (<c>EngineServices.Build</c> hands it the side's
    /// <see cref="EngineSide.Interfaces"/>), a named one in a fact.
    ///
    /// <para>
    /// <b>The seam is here so that what a REFUSED bind says can be tested at
    /// the call site rather than one layer under it.</b> A fact that exercised
    /// the exception helper directly would not have caught the defect that put
    /// this parameter here: the helper was right and this class called the
    /// wrong constructor, which is a difference only a test that goes through
    /// <c>Bind</c> can see.
    /// </para>
    /// </summary>
    public EngineFrame(EngineSide side, NativeInterfaceFactory factory)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _factory = factory;
    }

    /// <summary>
    /// The bound table, acquiring it if this is the first call.
    ///
    /// <para>
    /// NOT LOCKED, and the race is benign for the same reason the global-vars
    /// bind's is: two threads can acquire at once, both get the same registered
    /// singleton and the same table out of the same switch, and two
    /// pointer-sized writes of identical values are indistinguishable from one.
    /// </para>
    /// </summary>
    private ShimFrameFwd* Vtbl
    {
        get
        {
            if (_vtbl == null)
                Bind();

            return _vtbl;
        }
    }

    private void Bind()
    {
        // FromReport RATHER THAN THE CONSTRUCTOR, and a live run is what said
        // so: `why` is already a whole sentence with the factory kind and the
        // version string in front of it, so composing it again printed the
        // context twice, once per frame. See InterfaceAcquisitionException.
        if (!Forwarders.TryAcquire(
                _factory, SourceSharpFactoryType.Internal, InterfaceVersion,
                out void* self, out ShimFrameFwd* table, out string why))
            throw InterfaceAcquisitionException.FromReport(
                SourceSharpFactoryType.Internal, InterfaceVersion, why);

        _self = self;
        _vtbl = table;
    }

    /// <inheritdoc/>
    public bool IsSimulatingOnAlternateTicks()
        => Vtbl->IsSimulatingOnAlternateTicks(_self) != 0;

    /// <inheritdoc/>
    public int TickCount()
    {
        // The engine's own struct first — see the header comment. This is the
        // same order Engine.FrameTickCount used, moved here with the area.
        nint globals = _side.GlobalVarsPointer;

        return globals != 0 ? new Globals(globals).TickCount : Vtbl->TickCount(_self);
    }

    /// <inheritdoc/>
    public float GetFrameTime()
    {
        nint globals = _side.GlobalVarsPointer;

        return globals != 0 ? new Globals(globals).FrameTime : Vtbl->GetFrameTime(_self);
    }

    /// <inheritdoc/>
    // WRITING IS A CROSSING AND THE READ ABOVE IS NOT, which is the asymmetry
    // Engine.cs argued and this carries forward: reading frametime is the
    // relocation of a load, writing it is a mutation of the engine's own struct
    // in the middle of its frame.
    public void SetFrameTime(float frameTime) => Vtbl->SetFrameTime(_self, frameTime);

    /// <inheritdoc/>
    public void SetServerCurTime() => Vtbl->SetServerCurTime(_self);

    /// <inheritdoc/>
    public void ServerSystemsPreThink() => Vtbl->ServerSystemsPreThink(_self);

    /// <inheritdoc/>
    public void PhysicsRunThinkFunctions(bool simulating)
        => Vtbl->PhysicsRunThinkFunctions(_self, simulating ? 1 : 0);

    /// <inheritdoc/>
    public void ServerSystemsPostThink() => Vtbl->ServerSystemsPostThink(_self);

    /// <inheritdoc/>
    public void ServiceEventQueue() => Vtbl->ServiceEventQueue(_self);

    /// <inheritdoc/>
    public void UpdateAllClientData() => Vtbl->UpdateAllClientData(_self);

    /// <inheritdoc/>
    public void EndGameFrame() => Vtbl->EndGameFrame(_self);

    /// <inheritdoc/>
    public void FireNetworkPropertyEvents() => Vtbl->FireNetworkPropertyEvents(_self);

    /// <inheritdoc/>
    public void GamestatsUpdateConnection() => Vtbl->GamestatsUpdateConnection(_self);

    /// <inheritdoc/>
    public void DrawAllDebugOverlays() => Vtbl->DrawAllDebugOverlays(_self);

    /// <inheritdoc/>
    public void ServerSystemsPreClientUpdate() => Vtbl->ServerSystemsPreClientUpdate(_self);

    /// <inheritdoc/>
    public void StandalonePlayerCommands() => Vtbl->StandalonePlayerCommands(_self);

    /// <inheritdoc/>
    public void StandaloneUpdate() => Vtbl->StandaloneUpdate(_self);

    /// <inheritdoc/>
    public void StandaloneFinish() => Vtbl->StandaloneFinish(_self);

    /// <inheritdoc/>
    /// <remarks>
    /// THE ONE MEMBER OF THIS AREA THAT IS NOT A FRAME STEP, and the one that
    /// allocates. It runs per EVENT the managed directory could not place —
    /// never per frame — so the three encodes are paid by a misdelivery and by
    /// nothing else. A frame in which every input found a managed target does
    /// not reach this line at all.
    /// </remarks>
    public int DeliverToSdkEntities(string target, string input, string? value)
    {
        byte[] t = Vtable.Utf8(target);
        byte[] i = Vtable.Utf8(input);
        byte[] v = Vtable.Utf8(value ?? string.Empty);

        fixed (byte* pt = t)
        fixed (byte* pi = i)
        fixed (byte* pv = v)
            return Vtbl->DeliverToSdkEntities(_self, pt, pi, pv);
    }
}
