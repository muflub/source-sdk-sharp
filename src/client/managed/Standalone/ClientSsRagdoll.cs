//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Shared.Anim;

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
[ClientStandaloneEntity(EntityNames.Hl2mpRagdoll)]
public sealed class ClientSsRagdoll : ClientStandaloneEntity, ISaveCustom
{
    // The marked and custom tiers are the base's walk (plan_client_novalve
    // §3.8), which re-checks restored handles against the side's own host.
    public ClientSsRagdoll(IEngineEntityRender render, IEngineEffects effects, IGameClock clock,
                           IClientStandalones standalones, Ragdolls.IClientRagdolls ragdolls,
                           Ragdolls.IRagdollDissolves? dissolves = null, Ragdolls.IRagdollAsh? ash = null,
                           Ragdolls.IIceRagdolls? ice = null, Sounds.IClientEntitySounds? sounds = null,
                           Ragdolls.IRagdollFades? fades = null)
        : base(render)
    {
        _dissolves = dissolves;
        _ash = ash;
        _ice = ice;
        _sounds = sounds;
        _fades = fades;
        _effects = effects;
        _clock = clock;
        _standalones = standalones;
        _ragdolls = ragdolls;
    }

    private readonly IEngineEffects _effects;

    private readonly Ragdolls.IRagdollDissolves? _dissolves;

    private readonly Ragdolls.IRagdollAsh? _ash;

    private readonly Ragdolls.IIceRagdolls? _ice;

    private readonly Sounds.IClientEntitySounds? _sounds;

    private readonly Ragdolls.IRagdollFades? _fades;

    private readonly IGameClock _clock;

    private readonly IClientStandalones _standalones;

    // The client's bodies (plan_physics_bones_native_zero Phase 3): the body
    // this corpse collapses into is managed's, not a native library's.
    private readonly Ragdolls.IClientRagdolls _ragdolls;

    public override string ClassName => EntityNames.Hl2mpRagdoll;

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

    /// <summary>
    /// When this body's burn began, on THIS side's clock, or 0 for none.
    ///
    /// <para>
    /// Stock's <c>m_flBurnEffectStartTime</c>, stamped by the CLIENT at
    /// creation — <c>m_flBurnEffectStartTime = gpGlobals-&gt;curtime</c> in
    /// <c>CreateTFRagdoll</c> (c_tf_player.cpp:928-932) — for a body that
    /// arrived with its networked <c>m_bBurning</c> set
    /// (<see cref="RagdollNetState.Burning"/>). The clock crosses no wire: the
    /// server never knew it, and BurnLevel's ragdoll arm
    /// (c_tf_player.cpp:1860-1864) reads the one the ragdoll stamped for
    /// itself. Marked because it is a fact about this instance a hot reload
    /// would otherwise lose — a restored corpse that forgot its burn would
    /// ramp from never.
    /// </para>
    /// </summary>
    [SaveField] public float BurnStartTime { get; private set; }

    /// <summary>
    /// When this body's ash began, on THIS side's clock, or 0 for none — the
    /// ash arrival's <c>m_flTimeToDissolve</c> full, stamped by the client at
    /// <see cref="ArrivedAsh"/> for a body that arrived with
    /// <see cref="RagdollNetState.Ash"/> set and no gib. Like
    /// <see cref="BurnStartTime"/> the clock crosses no wire, and a restored
    /// corpse that forgot it would never hide.
    /// </summary>
    [SaveField] public float AshStartTime { get; private set; }

    public override void OnSpawn()
        => Log.Msg($"[SourceSharp/C# client] {ClassName} arrived\n");

    public override void OnRemoved()
    {
        _dissolves?.End(Handle);
        _ash?.End(Handle);
        _ice?.End(Handle);
        _fades?.End(Handle);

        if (BurnStartTime > 0f)
            _effects.ParticleSystemStop(Handle);

        if (AshStartTime > 0f)
            _effects.ParticleSystemStopSlot(Handle, Ragdolls.RagdollAsh.CorpseSlot);

        if (CritMistShown)
            _effects.ParticleSystemStopSlot(Handle, CritMistSlot);

        if (IceShown)
            _effects.ParticleSystemStopSlot(Handle, IceSlot);

        Log.Msg($"[SourceSharp/C# client] {ClassName} gone\n");
    }

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
        ArrivedBurning();
        ArrivedAsh();
        ArrivedCritMist();
        ArrivedIce();
        ArrivedGold();
        DressSkin();

        // THE VISIBLE CLOCK, armed at the arrival as stock's CreateTFRagdoll
        // arms it at the making (c_tf_player.cpp:863): the body fades
        // cl_ragdoll_fade_time after it appeared, whatever later becomes of
        // the server's entity. Begin keeps one entry per body, so the later
        // updates this walk serves again are the same arm.
        _fades?.Begin(Handle);

        if (IsRagdoll)
        {
            if (State.DissolveStart > 0f)
                _dissolves?.Begin(Handle, State.DissolveStart);
            return;
        }

        Attempts++;

        // POSED BEFORE IT IS COLLAPSED -- the pose is what the elements are
        // placed from, so the order matters.
        PosedAs = Pose();

        // A refusal is an ordinary answer and not an error: BecomeRagdoll says
        // no for a model with no jointed body in its .phy, which is most
        // models and would be a bug in what got spawned rather than here.
        // A gilded or frozen body is the exception stock builds too
        // (m_bFixedConstraints, c_tf_player.cpp:733 and :859): every joint
        // fixed, a statue and not a collapse -- the ice until its thaw
        // (RagdollIce), the gold for good.
        IsRagdoll = _ragdolls.Become(Handle, State.Force, State.ForceBone, out _,
                                     fixedConstraints: State.Gold || State.Ice);

        if (IsRagdoll && !State.Velocity.Equals(Vector.Zero))
            _ragdolls.AddVelocity(Handle, State.Velocity);

        if (IsRagdoll && State.DissolveStart > 0f)
            _dissolves?.Begin(Handle, State.DissolveStart);

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
                    $"thrown {State.Force}, moving {State.Velocity}\n");
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

    /// <summary>Stock's TF_TEAM_RED (ETFSharedTeam, tf_shareddefs.h).</summary>
    private const byte TeamRed = 2;

    /// <summary>The skin last handed to the renderer, so a decode that
    /// decided the same body writes nothing (ClientSsViewModel.ShowSkin's
    /// edge).</summary>
    private int _shownSkin = -1;

    /// <summary>
    /// Stock's <c>C_TFRagdoll::CreateTFRagdoll</c> skin arm
    /// (c_tf_player.cpp:712-719): the body wears the dead player's team skin —
    /// skin 0 for red, skin 1 otherwise — from the team the server stamped on
    /// it (<see cref="RagdollNetState.Team"/>). Without it every corpse draws
    /// in the model's skin 0 and a BLU death leaves a RED body.
    ///
    /// <para>
    /// ONLY A TF BODY, the one a class was stamped on
    /// (<see cref="RagdollNetState.Class"/>, TF2's death walk alone writes it).
    /// HL2MP's <c>C_HL2MPRagdoll</c> sets no skin, so its body wears its
    /// model's own, as the living player did: the TF rule turned an Unassigned
    /// or Rebel corpse to skin 1, a body that no longer looked like the player
    /// who died in it (plan_bots_teams_tf2 A.8).
    /// </para>
    /// </summary>
    private void DressSkin()
    {
        if (State.Class == 0)
            return;

        int skin = State.Team == TeamRed ? 0 : 1;

        if (skin == _shownSkin)
            return;

        _shownSkin = skin;
        Render.SetSkin(Handle, skin);
    }

    /// <summary>
    /// Stock's burn arrival (c_tf_player.cpp:928-932): a body that came
    /// burning stamps its own clock with the client's now and starts the
    /// corpse's particle. ONCE, wherever the update that carried the flag
    /// lands — the arrival may lose the model race and come back with the
    /// retries, and stock reaches this only from the created update.
    /// </summary>
    private void ArrivedBurning()
    {
        if (!State.Burning || BurnStartTime > 0f)
            return;

        BurnStartTime = _clock.Now;

        if (!_effects.ParticleSystemStart(Handle, FlameRules.BurningCorpseParticle))
            Log.Warning($"[SourceSharp] {ClassName}: no particle system named " +
                        $"'{FlameRules.BurningCorpseParticle}'\n");
    }

    /// <summary>
    /// Stock's ash arrival (c_tf_player.cpp:941-945): a body that came ashing —
    /// and neither gibed nor dissolving — stamps its half-second clock with the
    /// client's now and starts the ash particle, in a slot of its own so a
    /// burning corpse keeps its flames. ONCE, on the same terms as the burn's.
    /// Every update re-offers the hide to <see cref="RagdollAsh"/>, whose list
    /// a hot reload empties while this clock survives on the body — the same
    /// re-arm the dissolve's state-driven begin gets for free.
    /// </summary>
    private void ArrivedAsh()
    {
        if (!State.Ash || State.Gib)
            return;

        if (AshStartTime <= 0f)
        {
            AshStartTime = _clock.Now;

            if (!_effects.ParticleSystemStartSlot(Handle, FlameRules.AshCorpse, Ragdolls.RagdollAsh.CorpseSlot))
                Log.Warning($"[SourceSharp] {ClassName}: no particle system named " +
                            $"'{FlameRules.AshCorpse}'\n");
        }

        _ash?.Begin(Handle, AshStartTime);
    }

    /// <summary>Stock's red mist (c_tf_player.cpp:1120-1123): the gibbed body of
    /// a kill by a <c>crit_on_hard_hit</c> weapon — the Classic's — raises
    /// <c>tfc_sniper_mist</c> over the pieces. Stock dispatches it once inside
    /// <c>CreateTFGibs</c>, at the dying player's world centre; here the body
    /// entity is the same place, carried in a slot of its own so the burn's
    /// plain system and the ash's keep theirs. Once, on the flag's arrival —
    /// a gib never ashes (<see cref="ArrivedAsh"/>), so the two never run
    /// together.</summary>
    public const string CritMistParticle = "tfc_sniper_mist";

    /// <summary>The mist's slot, beside <see cref="RagdollAsh.CorpseSlot"/>'s 7.</summary>
    public const int CritMistSlot = 8;

    /// <summary>Whether this body's mist went up; the once-guard, kept across a
    /// save like <see cref="AshStartTime"/>.</summary>
    [SaveField] public bool CritMistShown { get; private set; }

    private void ArrivedCritMist()
    {
        if (!State.Gib || !State.CritOnHardHit || CritMistShown)
            return;

        CritMistShown = true;

        if (!_effects.ParticleSystemStartSlot(Handle, CritMistParticle, CritMistSlot))
            Log.Warning($"[SourceSharp] {ClassName}: no particle system named " +
                        $"'{CritMistParticle}'\n");
    }

    /// <summary>Stock's ice arrival (c_tf_player.cpp:736-742): a body frozen by a
    /// Spy-cicle backstab — the killing weapon's <c>freeze_backstab_victim</c>,
    /// read at <see cref="TFPlayerGib"/> — plays the turn-to-ice sound, raises
    /// stock's dry-ice frost over itself, and starts the frozen hold
    /// (<see cref="RagdollIce"/>, stock's m_frozenTimer at :741). Once, on the
    /// flag's arrival. The ice texture is <see cref="IceMaterial"/>; the statue
    /// itself is the collapse's shape, fixed like the gold's (:859 backs the
    /// midair arm) until <see cref="RagdollIce"/> thaws it.</summary>
    public const string IceSound = "Icicle.TurnToIce";

    public const string IceParticle = "xms_icicle_impact_dryice";

    /// <summary>Stock's ice texture, "we've been turned into an ice statue!"
    /// (c_tf_player.cpp:974-975). Held for the whole statue, thaw included —
    /// stock's single override outlives the frozen hold, and the statue thaws
    /// into a ragdoll of the same material (:1007-1025).</summary>
    public const string IceMaterial = "models/player/shared/ice_player";

    /// <summary>The frost's slot, beside the mist's <see cref="CritMistSlot"/>.</summary>
    public const int IceSlot = 9;

    /// <summary>Whether this body's frost went up; the once-guard, kept across a
    /// save like <see cref="CritMistShown"/>.</summary>
    [SaveField] public bool IceShown { get; private set; }

    private void ArrivedIce()
    {
        if (!State.Ice || IceShown)
            return;

        IceShown = true;

        _sounds?.EmitFrom(Handle, IceSound);

        Render.SetMaterialOverride(Handle, IceMaterial);
        _ice?.Begin(Handle, _clock.Now);

        if (!_effects.ParticleSystemStartSlot(Handle, IceParticle, IceSlot))
            Log.Warning($"[SourceSharp] {ClassName}: no particle system named " +
                        $"'{IceParticle}'\n");
    }

    /// <summary>Stock's gold arrival (c_tf_player.cpp:730-734): a body the Golden
    /// Wrench left — the killing weapon's <c>set_turn_to_gold</c>, read at
    /// <see cref="TFPlayerGib"/> — plays the turn-to-gold call and is built for
    /// the rest of the death as a statue. Once, on the flag's arrival; the
    /// statue itself is the collapse's shape, decided at
    /// <see cref="OnDataChanged"/>'s become. The gold material override over the
    /// body itself (:965-969) is <see cref="GoldMaterial"/>; stock's second arm,
    /// the same override pushed onto the followed wearables (:983-994), has no
    /// bodies to dress here — this tree's wearables die with their player
    /// (TFWearables' death arm), so nothing follows the corpse.</summary>
    public const string GoldSound = "Saxxy.TurnGold";

    /// <summary>Stock's gold texture, "we've been turned into a golden corpse!"
    /// (c_tf_player.cpp:967-968).</summary>
    public const string GoldMaterial = "models/player/shared/gold_player";

    /// <summary>Whether this body's gilding was announced; the once-guard, kept
    /// across a save like <see cref="IceShown"/>.</summary>
    [SaveField] public bool GoldShown { get; private set; }

    private void ArrivedGold()
    {
        if (!State.Gold || GoldShown)
            return;

        GoldShown = true;

        _sounds?.EmitFrom(Handle, GoldSound);
        Render.SetMaterialOverride(Handle, GoldMaterial);
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

        // A body no player left names the sequence it stood in
        // (RagdollNetState.Sequence): the server's own last pose for it.
        if (sequence < 0 && State.Sequence > 0)
            sequence = State.Sequence;

        if (sequence < 0)
        {
            foreach (string activity in Hl2PostureLayer.Activities(PlayerPosture.Idle))
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
