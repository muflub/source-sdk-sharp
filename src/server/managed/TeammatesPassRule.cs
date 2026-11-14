using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// "Teammates collide" (plan A.7): in a mode whose teammates walk through each
/// other, 1 makes them block again. Default off, effect at once (the rule
/// reads it per trace). Replicated: the client's prediction reads the answer
/// it makes through <c>mp_teammates_pass</c>.
/// </summary>
[ConVar("mp_teammates_collide", "0", Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "In a team mode whose teammates walk through each other, 1 makes teammates block each other again")]
public sealed class MpTeammatesCollideConVar : BoolConVar { }

/// <summary>
/// Whether teammates walk through each other right now: written by the server
/// each frame from the active mode's <see cref="IGameMode.TeammatesPass"/> and
/// <c>mp_teammates_collide</c> (<see cref="TeammatesPassRule"/>), and
/// replicated so the client's prediction passes the same players the server
/// does. Not a setting: a value typed here is put back the next frame.
/// </summary>
[ConVar("mp_teammates_pass", "0", Flags = ConVarFlags.Replicated,
        Help = "Set by the server from the active mode and mp_teammates_collide: whether teammates walk through each other")]
public sealed class MpTeammatesPassConVar : BoolConVar { }

/// <summary>
/// Whether overlapping teammates push each other apart (plan A.7, stock TF2's
/// <c>tf_avoidteammates_pushaway</c>, default on there too). Read only while
/// teammates pass.
/// </summary>
[ConVar("mp_teammates_pushaway", "1", Flags = ConVarFlags.Replicated,
        Help = "Whether teammates who walk through each other push each other apart when they overlap")]
public sealed class MpTeammatesPushawayConVar : BoolConVar { }

/// <summary>
/// The core's collide rule (plan A.7): a player-movement trace of a player
/// passes through a teammate (both on the same game team) while the active
/// mode answers <see cref="IGameMode.TeammatesPass"/> and
/// <c>mp_teammates_collide</c> is off. Only movement: a bullet, a blast or a
/// physics trace carries another group and still hits the teammate. Stock
/// HL2DM's <c>CHL2MPRules::ShouldCollide</c> has no team test (a divergence,
/// docs/divergences.md); deathmatch's mode answers false, so it is unchanged.
///
/// <para>
/// Also the publisher of the answer to the client: once a frame, after the
/// mode proxy has settled the active mode, it writes <c>mp_teammates_pass</c>
/// when the answer changed, and the engine replicates it.
/// </para>
/// </summary>
public sealed class TeammatesPassRule(GameModes modes, MpTeammatesCollideConVar collide, MpTeammatesPassConVar published,
                                      IEnginePlayers players) : IEntityCollideRule, IOrderedHook, IFrameLifecycle
{
    /// <summary>
    /// The core's place on the collide chain: first, and never 0, so a mod's
    /// rule that leaves its order at 0 (TF2's <c>TFTeamCollide</c>) does not
    /// tie with it. Every rule must agree, so the order changes no answer.
    /// </summary>
    public const int Order = -1000;

    /// <summary>Whether teammates pass now: the active mode says so and the setting does not refuse it.</summary>
    public bool TeammatesPass => !collide.Value && modes.Active is { TeammatesPass: true };

    public int HookOrder => Order;

    public bool ShouldCollide(EntityHandle mover, EntityHandle entity, CollisionGroup group, int contentsMask)
    {
        // The cheap refusals first: every trace of every candidate asks.
        if ((int)group != TeammateCollision.PlayerMovementGroup || mover == EntityHandle.None || !TeammatesPass)
            return true;

        return !TeammateCollision.Passes(true, (int)group, players.GetTeam(mover), players.GetTeam(entity));
    }

    int IFrameLifecycle.FrameOrder => (int)FrameStage.TeammateCollision;

    void IFrameLifecycle.OnThink(float frameTime) => Publish();

    /// <summary>Writes the answer to <c>mp_teammates_pass</c> when it differs from what the clients were told.</summary>
    public void Publish()
    {
        bool pass = TeammatesPass;
        if (published.Value != pass)
            published.SetValue(pass);
    }
}
