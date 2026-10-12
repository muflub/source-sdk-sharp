//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Leaves a body where a player died.
///
/// <para>
/// This is <c>CHL2MP_Player::CreateRagdollEntity</c>, which stock calls from
/// <c>Event_Killed</c> before it chains up. It is a SERVICE rather than
/// something <c>SsPlayer.OnKilled</c> does inline, for the same reason
/// <see cref="Pickups"/> is one: making a corpse needs three engine areas the
/// player entity has no other use for, and the policy questions it answers —
/// how many bodies may exist at once, what a body is made of — are about the
/// game as a whole rather than about one player.
/// </para>
///
/// <para>
/// WHAT IT DOES NOT DO is decide whether the corpse becomes a ragdoll. That is
/// the client's answer to give (<c>ClientSsRagdoll</c>), because the model's
/// collision data is what decides it and only a machine that has the model
/// loaded can look. This side spawns a body and says what threw it.
/// </para>
/// </summary>
public sealed class Corpses : IGameLifecycle
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    // ...and the bodies with them, for the same reason as the pickups.
    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.Corpses;

    void IGameLifecycle.OnLevelInit(string mapName)
    {
        Reset();
        ReadViolencePolicy(mapName);
    }

    private readonly IEngineEntities _entities;
    private readonly IEngineEntityRender _render;
    private readonly IEntityDirectory _directory;
    private readonly IEngineConsole? _console;

    /// <summary>The bodies currently standing, oldest first. See <see cref="Limit"/>.</summary>
    private readonly List<EntityHandle> _live = new();

    public Corpses(ILog log, IEngineEntities entities, IEngineEntityRender render,
                   IEntityDirectory directory, IEngineConsole? console = null)
    {

        _log = log;
        _entities = entities;
        _render = render;
        _directory = directory;
        _console = console;
    }

    /// <summary>
    /// Whether this map is showing bodies at all —
    /// <c>g_RagdollLVManager.IsLowViolence()</c>, which lane p4-anim moved out
    /// of <c>game/shared/ragdoll_shared.cpp</c> into
    /// <see cref="RagdollViolencePolicy"/>.
    ///
    /// <para>
    /// VACUOUS ON THIS SIDE TODAY, and said out loud rather than left to be
    /// found. Stock's own rule is <i>"the server doesn't worry about low
    /// violence during multiplayer games"</i>, and this mod is a deathmatch:
    /// <c>isMultiplayer</c> is true on every map, so the composed answer is
    /// <c>false</c> for every map that is not on the citadel list — which no
    /// map of this mod is. That is EXACTLY what the native manager answered
    /// before, for the same reason, so nothing changed and nothing is claimed
    /// to have.
    /// </para>
    ///
    /// <para>
    /// It is wired anyway because the rule is the rule and the CLIENT half is
    /// not vacuous — <c>c_baseanimating.cpp:523</c> picks a fade speed off it —
    /// and because leaving the flag unwritten would have made
    /// <see cref="Trim"/>'s zero-cap arm a branch nothing could reach, which is
    /// the "a field nothing writes is a field nothing checks" shape step 4d
    /// recorded.
    /// </para>
    /// </summary>
    public bool LowViolence { get; private set; }

    /// <summary>
    /// Composes <see cref="LowViolence"/> for a map, out of the four legacy
    /// ConVars and the map list.
    ///
    /// <para>
    /// FOUR OF THE FIVE INPUTS. <c>engine-&gt;IsLowViolence()</c> has no
    /// managed door on the server — see <see cref="RagdollViolencePolicy"/> for
    /// the slot it wants and why it is designed rather than requested — so it
    /// is passed as <c>false</c> here. On a build where the engine is the only
    /// thing set low, this answers <c>false</c> where the native manager
    /// answered <c>true</c>; on this mod both are <c>false</c> anyway because
    /// the multiplayer clause clears it either way.
    /// </para>
    /// </summary>
    private void ReadViolencePolicy(string mapName)
    {
        bool baseLow = RagdollViolencePolicy.IsLowViolence(
            ConVarOn("violence_hblood"), ConVarOn("violence_ablood"),
            ConVarOn("violence_hgibs"), ConVarOn("violence_agibs"),
            engineLowViolence: false);

        LowViolence = RagdollViolencePolicy.LowViolenceForMap(
            baseLow, isServer: true, isMultiplayer: true, episodic: false, mapName);
    }

    /// <summary>A ConVar read that defaults to ON, which is every one of the four's own default.</summary>
    private bool ConVarOn(string name)
    {
        string? value = _console?.FindConVarValue(name);

        if (string.IsNullOrEmpty(value))
            return true;

        return !(value == "0" || value.StartsWith("0.", StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether death leaves a body at all.
    ///
    /// <para>
    /// A rule about how the game looks rather than a property of a player,
    /// which is why it is here — and it is the switch that says what
    /// <see cref="Make"/> costs, since turning it off is the only way to get
    /// the old behaviour back.
    /// </para>
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The most bodies that may exist at once. The oldest goes when a new one
    /// would exceed it.
    ///
    /// <para>
    /// A CAP AND NOT ONLY A TIMER, because the two bound different things.
    /// <see cref="SsRagdoll.Lifetime"/> bounds how long one body lasts; this
    /// bounds how many a busy firefight can pile up inside that time, which on
    /// a full server is a much larger number than the map ever has players.
    /// Stock's equivalent is <c>g_ragdoll_maxcount</c>, which stock's
    /// <c>ragdoll_shared.cpp</c> defaults to EIGHT — and this is twelve,
    /// deliberately: eight is a single-player number for a map with one player
    /// in it. The rule this feeds is stock's
    /// (<see cref="RagdollRetirementPolicy.Retire"/>); the number is not, and
    /// the difference is recorded here rather than hidden in the policy, which
    /// carries stock's default as <c>DefaultMaxCount</c> for anyone comparing.
    /// </para>
    /// </summary>
    public int Limit { get; set; } = 12;

    /// <summary>How many bodies are standing. For tests and for the report.</summary>
    public int Count => _live.Count;

    /// <summary>
    /// Spawns the body a player left behind, and returns it —
    /// <see cref="EntityHandle.None"/> if none was made.
    ///
    /// <para>
    /// EVERYTHING IS READ FROM THE PLAYER rather than passed in, and that is
    /// deliberate: the model, the place, the facing and the velocity have to be
    /// the ones the player had at the instant they died, and a caller that
    /// gathered them separately could gather them a frame late. Stock reads the
    /// same four off <c>this</c> in <c>CreateRagdollEntity</c> for the same
    /// reason.
    /// </para>
    /// </summary>
    public EntityHandle Make(EntityHandle player)
    {
        if (!Enabled)
            return EntityHandle.None;

        // A CAP OF ZERO MEANS NO BODY IS MADE, not one made and swept next
        // frame. Stock reaches the same place by a different road -- its cap
        // is applied in Update, so a low-violence map builds the ragdoll and
        // fades it immediately -- and the difference is one frame of a corpse
        // on a map whose whole point is not to show one. Refusing here is the
        // cheaper and the more faithful of the two.
        if (RagdollRetirementPolicy.EffectiveCap(-1, Limit, LowViolence) <= 0)
            return EntityHandle.None;

        // READ BEFORE ANYTHING IS SPAWNED. Creating an entity can run map
        // logic, and a corpse that took its origin afterwards would be
        // recording where the player was after that ran rather than where they
        // died.
        string model = _render.GetModelName(player);
        Vector origin = _entities.GetOrigin(player);
        Vector angles = _entities.GetAngles(player);
        Vector velocity = _entities.GetVelocity(player);

        // A player with no model has nothing to make a body out of. This is
        // reported rather than passed on, because a ragdoll is one of the few
        // things that fails SILENTLY further down -- BecomeRagdoll refuses a
        // model it cannot find and the corpse simply stands there.
        if (string.IsNullOrEmpty(model))
        {
            _log.Warning($"[SourceSharp/Corpse] {player} died with no model; no body left\n");
            return EntityHandle.None;
        }

        Forget();
        Trim();

        var body = _entities.Create(EntityNames.SsRagdoll);

        // COMPARED AGAINST None RATHER THAN ASKED WHETHER IT IsValid, which is
        // the same trap Players.OnKilled records: EntityHandle.IsValid asks the
        // STATIC engine, so it is right in a game and answers false in a test —
        // and a refusal here means every corpse silently stops being made
        // wherever nobody is watching. Create's failure IS None.
        if (body == EntityHandle.None)
        {
            _log.Warning($"[SourceSharp/Corpse] could not create a body for {player}\n");
            return EntityHandle.None;
        }

        // Model, place and facing BEFORE the spawn, for the reason Pickups
        // gives at the same point: spawning is what reads what was set
        // beforehand, which is the whole reason creation and spawning are two
        // calls. The client's collapse starts from this pose.
        _render.SetModel(body, model);
        _entities.SetOrigin(body, origin);
        _entities.SetAngles(body, angles);

        _entities.Spawn(body);

        // THE IMPULSE IS SET AFTER THE SPAWN, and it has to be: spawning is
        // what binds the managed SsRagdoll instance (the same ordering
        // Pickups records), so before this call there is no State to write to.
        // It still reaches the first update -- a datatable is sent at the end
        // of the frame, not at the moment of spawn.
        if (_directory.Get(body) is SsRagdoll ragdoll)
        {
            ragdoll.State.Force = velocity;

            // CENTRE OF MASS, because this mod has no per-hit bone. Stock
            // fills m_nForceBone from the trace that killed you; the managed
            // damage path carries neither a bone nor a direction (see
            // RagdollNetState), so naming a bone here would be inventing one.
            ragdoll.State.ForceBone = -1;

            // WHO THIS BODY WAS, so the client can collapse it from the pose
            // they were standing in rather than from the model's reference
            // skeleton -- which comes out as a stretched smear. See
            // RagdollNetState.PlayerIndex; stock networks m_hPlayer for this.
            ragdoll.State.PlayerIndex = (short)_entities.GetIndex(player);

            // WHERE IT COLLAPSES, carried even though the entity has an origin
            // of its own -- see RagdollNetState.Origin. The entity's origin is
            // not applied yet when the client's first update lands, so without
            // these two the body is built at the map origin and drawn stretched
            // between there and here.
            ragdoll.State.Origin = origin;
        }
        else
        {
            // Loud, because the body will still appear and will simply not be
            // thrown -- a difference nobody would spot in a game.
            _log.Warning($"[SourceSharp/Corpse] {body} has no managed SsRagdoll; " +
                        "the body will not carry its death velocity\n");
        }

        _live.Add(body);

        _log.Msg($"[SourceSharp/Corpse] {player} left a body at {origin} " +
                $"({_live.Count} of {Limit})\n");

        return body;
    }

    /// <summary>
    /// Drops handles whose entity is gone — removed by
    /// <see cref="SsRagdoll.OnThink"/> or by a level change.
    ///
    /// <para>
    /// Needed because the cap counts BODIES and not entries: without this the
    /// list fills with handles to nothing and the limit starts removing live
    /// bodies to make room for the dead ones it is still counting.
    /// </para>
    ///
    /// <para>
    /// Asked through the INJECTED interface rather than <c>handle.IsValid</c>,
    /// for the reason given at the creation check: the property on the handle
    /// goes to the static engine and a test's double never sees it.
    /// </para>
    /// </summary>
    private void Forget() => _live.RemoveAll(h => !_entities.IsValid(h));

    /// <summary>
    /// Keeps a body this service made until the level ends: out of the
    /// <see cref="Limit"/> sweep, and past <see cref="SsRagdoll.Lifetime"/>.
    /// For a mod whose dead stay on the ground (TF2Descent's corpses). Returns
    /// false for a body this service is not holding.
    /// </summary>
    public bool Keep(EntityHandle body)
    {
        if (!_live.Remove(body))
            return false;

        if (_directory.Get(body) is SsRagdoll ragdoll)
            ragdoll.Kept = true;
        else
            _log.Warning($"[SourceSharp/Corpse] {body} has no managed SsRagdoll; it will still go at its lifetime\n");

        return true;
    }

    /// <summary>
    /// Removes the oldest bodies until there is room for one more.
    ///
    /// <para>
    /// THE DECISION IS <see cref="RagdollRetirementPolicy"/>'S and no longer
    /// this method's. What was here was a bare <c>while (count &gt;= Limit)</c>,
    /// which is stock's sweep with two rules missing: the low-violence cap of
    /// zero, and the skip for a body an effect entity still owns. Both are in
    /// the policy now, so both apply here — see
    /// <see cref="StillEffected"/> for what this side can answer about the
    /// second one today.
    /// </para>
    ///
    /// <para>
    /// The cap handed over is one SMALLER than the standing cap, because this
    /// runs before the new body is added and stock's runs after: "leave room
    /// for one" and "sweep down to the cap" are the same rule a frame apart.
    /// </para>
    /// </summary>
    private void Trim()
    {
        int cap = RagdollRetirementPolicy.EffectiveCap(-1, Limit, LowViolence);
        int room = cap > 0 ? cap - 1 : 0;

        var effected = new bool[_live.Count];

        for (int i = 0; i < _live.Count; i++)
            effected[i] = StillEffected(_live[i]);

        List<int> going = RagdollRetirementPolicy.Retire(effected, room);

        // BACKWARDS, so an index the policy handed back still names the same
        // body after the ones before it have gone.
        for (int i = going.Count - 1; i >= 0; i--)
        {
            var body = _live[going[i]];
            _live.RemoveAt(going[i]);

            if (_entities.IsValid(body))
                _entities.Remove(body);
        }
    }

    /// <summary>
    /// Stock's <c>pRagdoll-&gt;GetEffectEntity()</c>: is a body still burning
    /// or dissolving, and therefore not to be swept.
    ///
    /// <para>
    /// ALWAYS FALSE ON THIS SIDE TODAY, and that is a measurement rather than a
    /// stub. An effect entity is a <c>CEntityFlame</c> or a
    /// <c>CEntityDissolve</c> parented to the body, and neither reaches a
    /// managed corpse: <c>SourceSharp_EntityDissolve</c> is a declared refusal
    /// (step 4a, <c>ContractBaseline.Refusals</c>) and nothing in
    /// <c>SourceSharp.Server</c> ignites an <c>ss_ragdoll</c>. The moment
    /// either lands, this is the one line that has to answer, and it is named
    /// so the next lane finds it instead of rediscovering the rule.
    /// </para>
    /// </summary>
    private bool StillEffected(EntityHandle body) => false;

    /// <summary>
    /// Forgets every body, at level change.
    ///
    /// The same reason <see cref="Players.Reset"/> exists: handles do not
    /// survive a map change, and keeping them would leave this holding entries
    /// for entities whose handles may since have been reused by something else.
    /// </summary>
    public void Reset() => _live.Clear();
}
