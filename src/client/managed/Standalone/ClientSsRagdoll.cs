//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The viewer's half of a corpse — the class that actually makes a ragdoll.
///
/// <para>
/// One of these exists per viewer per body, and each builds its own jointed
/// simulation locally. That is stock's arrangement, not a shortcut around
/// networking one: <c>C_HL2MPRagdoll::OnDataChanged</c> calls
/// <c>CreateHL2MPRagdoll</c> on <c>DATA_UPDATE_CREATED</c> and every client
/// runs the same collapse. What crosses the wire is a model, a place and an
/// impulse; what does not cross is twenty-four bone transforms a tick.
/// </para>
///
/// <para>
/// RETRIED RATHER THAN ATTEMPTED ONCE, which is the one place this does more
/// than stock and the reason is written down in the sibling class:
/// <c>ClientSsPlayer</c> notes that "a model can arrive after the entity
/// does". <c>BecomeRagdoll</c> needs the model — it reads the collision data
/// to find out whether there is a jointed body in it at all — so a create-only
/// attempt that lost that race would leave a body standing rigidly to
/// attention forever, with nothing anywhere saying why. Asking again on each
/// update until one takes costs a handful of refused calls in the worst case
/// and nothing in the ordinary one.
/// </para>
/// </summary>
///
/// <para>
/// AND IT SURVIVES A HOT RELOAD, which is step 6e's second gate clause and the
/// reason this class carries state at all. The body's vphysics objects are not
/// unloaded with the managed side, and the ragdolls' table crosses the reload
/// pointer for pointer (ClientRagdolls is a reload participant), so what a
/// reload actually destroys is this object's KNOWLEDGE of the body: whether it
/// collapsed, what it collapsed from, and how many attempts it took. Rebuilt with all of that at zero, the
/// new instance would re-pose a body the solver already owns and then report
/// "collapsed after 1 attempt" about a corpse that fell over a minute ago.
/// </para>
///
/// <para>
/// So the state goes across in the two tiers this tree already has for it:
/// <b>marked</b> (<c>[SaveField]</c>) for the four facts, and <b>custom</b>
/// (<see cref="ISaveCustom"/>) for the one thing no declaration can describe —
/// where the SOLVER had the body at the moment of the save, which is what makes
/// "the same body, in the same pose" a check rather than a hope.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.SsRagdoll)]
public sealed class ClientSsRagdoll : ClientStandaloneEntity, ISaveCustom
{
    // The marked and custom tiers are the base's walk (plan_client_novalve
    // §3.8), which re-checks restored handles against the side's own host.
    public ClientSsRagdoll(IEngineEntityRender render, IClientStandalones standalones, Ragdolls.IClientRagdolls ragdolls)
        : base(render)
    {
        _standalones = standalones;
        _ragdolls = ragdolls;
    }

    private readonly IClientStandalones _standalones;

    // The client's bodies (plan_physics_bones_native_zero Phase 3): the body
    // this corpse collapses into is managed's, not a native library's.
    private readonly Ragdolls.IClientRagdolls _ragdolls;

    public override string ClassName => EntityNames.SsRagdoll;

    /// <summary>The same type the server writes through. See <see cref="RagdollNetState"/>.</summary>
    [NetState] public RagdollNetState State = new();

    /// <summary>
    /// Whether this body has collapsed yet.
    ///
    /// <para>
    /// Public because it is the only externally visible fact about this class
    /// — a corpse that never became a ragdoll looks like a corpse that did,
    /// from everywhere except here.
    /// </para>
    /// </summary>
    [SaveField] public bool IsRagdoll { get; private set; }

    /// <summary>How many times the engine has been asked. See the note on retrying.</summary>
    [SaveField] public int Attempts { get; private set; }

    /// <summary>
    /// Whether the pose came from the dead player themselves rather than from
    /// the idle fallback.
    ///
    /// <para>
    /// Reported because the two are INDISTINGUISHABLE in the common case — a
    /// player killed standing still is already in the idle, so both paths name
    /// the same sequence — and that is exactly the case a probe kills them in.
    /// The lookup answered nothing at all until the client learned its own
    /// index, and nothing said so.
    /// </para>
    /// </summary>
    [SaveField] public bool PosedFromThePlayer { get; private set; }

    /// <summary>
    /// The sequence this body was posed in before it collapsed, or -1 if it
    /// could not be posed at all.
    ///
    /// <para>
    /// Public because the pose is the difference between a body and a smear,
    /// and it is otherwise invisible: a corpse collapsed from the reference
    /// skeleton is a successful <c>BecomeRagdoll</c> that looks like a physics
    /// bug.
    /// </para>
    /// </summary>
    [SaveField] public int PosedAs { get; private set; } = -1;

    /// <summary>
    /// The player whose pose this body collapsed from, or
    /// <see cref="EntityHandle.None"/> when it was posed from an idle. Stock's
    /// <c>C_HL2MPRagdoll::m_hPlayer</c>, held here rather than networked.
    ///
    /// <para>
    /// It is the client's one saved handle, and so the readout for whether a
    /// <c>cs_cl_reload</c> brings handles back: the restore re-checks it
    /// against the client's own entity list (<c>SavedHandles</c>). While
    /// <c>EntityIsValid</c> was server-only that check answered false here and
    /// this came back None for a player who was still there.
    /// </para>
    /// </summary>
    [SaveField] public EntityHandle PosedFrom { get; private set; } = EntityHandle.None;

    public override void OnSpawn()
        => Log.Msg($"[SourceSharp/C# client] {ClassName} arrived\n");

    public override void OnRemoved()
        => Log.Msg($"[SourceSharp/C# client] {ClassName} gone\n");

    /// <summary>
    /// Collapses the body, once.
    ///
    /// <para>
    /// The update type is deliberately NOT tested. Stock gates on
    /// <c>DATA_UPDATE_CREATED</c> because that is the only update where its
    /// state is fresh; here the guard that matters is
    /// <see cref="IsRagdoll"/>, which says the thing the gate was really for —
    /// build one body and no more. Gating on the type as well would reintroduce
    /// exactly the race the retry exists to survive.
    /// </para>
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (IsRagdoll)
            return;

        Attempts++;

        // POSED BEFORE IT IS COLLAPSED -- the pose is what the elements are
        // placed from, so the order matters.
        PosedAs = Pose();

        // A refusal is an ordinary answer and not an error: BecomeRagdoll says
        // no for a model with no jointed body in its .phy, which is most
        // models and would be a bug in what got spawned rather than here.
        IsRagdoll = _ragdolls.Become(Handle, State.Force, State.ForceBone, out _);

        // SAID OUT LOUD, because "a body arrived" and "a body collapsed" look
        // identical from everywhere else — the entity exists, is drawn, and is
        // removed on time either way. A corpse standing rigidly to attention
        // is the failure this class exists to avoid, and without a line here
        // the only way to see it is to be looking at the screen.
        if (IsRagdoll)
        {
            Log.Msg($"[SourceSharp/C# client] {ClassName} collapsed after {Attempts} " +
                    $"attempt(s) from sequence {PosedAs} " +
                    $"({(PosedFromThePlayer ? $"the player's own pose, handle 0x{PosedFrom.Value:X8}" : "an idle")}) " +
                    $"at {State.Origin}, " +
                    $"thrown {State.Force}\n");
        }
        else if (Attempts == 1)
        {
            // Once, on the first refusal. Every update retries, and a line per
            // update would bury the log of a body that never collapses --
            // which is precisely the case worth reading.
            Log.Msg($"[SourceSharp/C# client] {ClassName} has no jointed body yet; " +
                    "retrying on each update\n");
        }
    }

    /// <summary>
    /// Puts the body into a real animation pose, and answers which one.
    ///
    /// <para>
    /// THIS IS NOT COSMETIC. A <c>.phy</c>'s constraint limits are authored
    /// against a posed skeleton, so a ragdoll built from the model's REFERENCE
    /// pose — arms out, which is what sequence 0 draws and what the shell
    /// defaults to for an entity nothing has ever animated — starts every joint
    /// outside its limits, and the first constraint solve tears the body into a
    /// stretched smear across the floor. It is a successful
    /// <c>BecomeRagdoll</c> that looks like a physics bug.
    /// </para>
    ///
    /// <para>
    /// THE PLAYER'S OWN POSE FIRST, so a body collapses out of the stride it
    /// died in. This is stock's local-player branch exactly —
    /// <c>SetSequence( iSeq ); SetCycle( 0.0 );</c> in
    /// <c>C_HL2MPRagdoll::CreateHL2MPRagdoll</c>, whose comment names it
    /// "walk_lower, basic pose" — rather than its remote-player branch, which
    /// also copies the animation time and playback rate through
    /// <c>Interp_Copy</c>. A rate of zero and a restart is what gives cycle
    /// zero here.
    /// </para>
    ///
    /// <para>
    /// AND AN IDLE IF THE PLAYER IS NOT THERE — dormant, out of PVS, or simply
    /// not decoded yet on this machine. Stock's equivalent branch falls back to
    /// sequence 0, which is the reference pose and the very thing that breaks;
    /// resolving an activity is this tree's own idiom (<c>ClientSsPlayer.Resolve</c>
    /// does it for the same reason) and gives a real pose instead.
    /// </para>
    /// </summary>
    private int Pose()
    {
        int sequence = PlayersOwnPose(out EntityHandle player);

        PosedFromThePlayer = sequence >= 0;
        PosedFrom = PosedFromThePlayer ? player : EntityHandle.None;

        if (sequence < 0)
        {
            foreach (string activity in PlayerAnimation.Activities(PlayerPosture.Idle))
            {
                sequence = Render.LookupSequenceByActivity(Handle, activity);

                if (sequence >= 0)
                    break;
            }
        }

        // -1 means the model answers to no activity at all. The body is left
        // alone rather than forced to sequence 0: a model with no animation has
        // no pose to collapse from, and BecomeRagdoll refuses it below anyway.
        if (sequence >= 0)
            Render.PlaySequence(Handle, sequence, playbackRate: 0f, restart: true);

        return sequence;
    }

    //-------------------------------------------------------------------------
    // THE HOT RELOAD. plan_entity.md 6e's second gate clause.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Where the SOLVER had this body when the reload began, and how far it has
    /// moved since. Both are -1/none for a body that was never collapsed.
    ///
    /// <para>
    /// Public because they are the only externally visible evidence that the
    /// body which came back is the one that went in. A count of ragdolls is the
    /// same on both sides of a reload that destroyed every one of them and built
    /// new ones — which is exactly the shape of failure the strengthened reload
    /// gate found elsewhere, where 478 entities were saved and -1 restored with
    /// every count check green.
    /// </para>
    /// </summary>
    public Vector PoseBeforeReload { get; private set; }

    /// <summary>How far the body moved across the reload, or -1 if it did not survive.</summary>
    public float PoseDriftAcrossReload { get; private set; } = -1f;

    /// <summary>Whether the body this instance addresses is the one it addressed before.</summary>
    public bool SurvivedReload { get; private set; }

    /// <summary>
    /// WHERE THE SOLVER HAS IT, which is the one fact no declaration can carry.
    ///
    /// <para>
    /// It is not a field of this class and never was: it lives in the solver, is
    /// read back through <c>RagdollState</c>, and changes every physics step. A
    /// <c>[SaveField]</c> over a property that asked the engine would work by
    /// accident — the marked tier reads at save time, which happens to be right
    /// — and would silently stop working the moment anything cached it. This
    /// says what it is doing instead.
    /// </para>
    /// </summary>
    public void OnSave(ISaveWriter writer)
    {
        RagdollState live = _ragdolls.State(Handle, out Vector origin);

        writer.Write("present", (live & RagdollState.Present) != 0);
        writer.Write("origin", origin);
    }

    /// <inheritdoc cref="OnSave"/>
    public void OnRestore(ISaveReader reader)
    {
        bool wasPresent = reader.ReadBool("present");

        PoseBeforeReload = reader.ReadVector("origin");

        RagdollState live = _ragdolls.State(Handle, out Vector now);

        SurvivedReload = wasPresent && (live & RagdollState.Present) != 0;

        // THE DRIFT, AND NOT "IS THERE A RAGDOLL". A body that died during the
        // swap and was rebuilt from the pose would be Present too, standing
        // where the entity's networked origin is -- which for a corpse is where
        // it was killed and not where it fell. The distance is what tells the
        // two apart, and it is reported rather than judged: how far a settling
        // body moves in the time a reload takes is not this class's business.
        PoseDriftAcrossReload = SurvivedReload ? (now - PoseBeforeReload).Length : -1f;

        // SAID OUT LOUD, because from everywhere else a body that survived and a
        // body that was silently rebuilt look identical: the entity exists, is
        // drawn, and is removed on time either way.
        if (SurvivedReload)
        {
            Log.Msg($"[SourceSharp/C# client] {ClassName} survived the reload: the same body, " +
                    $"{PoseDriftAcrossReload:F2} units from where the solver had it, still " +
                    $"posed from sequence {PosedAs} after {Attempts} attempt(s), " +
                    $"posed-from handle 0x{PosedFrom.Value:X8}\n");
        }
        else if (wasPresent)
        {
            Log.Warning($"[SourceSharp/C# client] {ClassName} had a jointed body before the " +
                        "reload and has none after it. The ragdolls' table crosses the reload, " +
                        "so this is a body that was destroyed or a table that was not carried\n");
        }

        // AND THE HANDLE, which the restore re-checked against this side's
        // list: a body posed from a player who is still here must come back
        // knowing who. None here is the restore losing it, not the player
        // leaving -- the server keeps a dead player's entity.
        if (PosedFromThePlayer && PosedFrom == EntityHandle.None)
        {
            Log.Warning($"[SourceSharp/C# client] {ClassName} was posed from the player but the " +
                        "reload lost the handle it was posed from\n");
        }
    }

    /// <summary>
    /// The sequence the dead player's body was in, or -1 if this machine
    /// cannot see them.
    /// </summary>
    /// <remarks>
    /// This answered NOTHING until the client learned its own index.
    /// <c>EntityGetIndex</c> was a server-only slot, so <c>Handle.Index</c> was
    /// -1 over here and <c>ClientStandaloneRegistry</c>'s index map — which
    /// records an instance only when the index is zero or more — was always
    /// empty, so the lookup always missed and every body collapsed from the
    /// idle below. The slot is dual-wired now and this is the path that
    /// ordinarily runs; the idle is the fallback it was written to be.
    /// </remarks>
    private int PlayersOwnPose(out EntityHandle handle)
    {
        handle = EntityHandle.None;

        if (State.PlayerIndex < 0)
            return -1;

        if (_standalones.GetByIndex(State.PlayerIndex) is not ClientSsPlayer player)
            return -1;

        handle = player.Handle;
        return player.ShownSequence;
    }
}
