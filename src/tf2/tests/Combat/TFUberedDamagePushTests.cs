using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace TF2.Tests.Combat;

/// <summary>
/// The übered victim's events-only answer (stock's <c>DAMAGE_EVENTS_ONLY</c>,
/// <c>shared/tf/tf_gamerules.cpp:6366-6391</c>): a rocket blast on an übered
/// player leaves the health whole but still lands the hit's events and the
/// damage push; a non-übered victim is unchanged by the arm. The facts drive
/// the real chain — <see cref="GameRules.CanTakeDamage"/>' rule chain,
/// <see cref="TFDamageRules"/>' über answer, the modifier fold and the
/// player's own damage path — through <c>OnDamagedAt</c>, as a press does.
/// </summary>
public sealed class TFUberedDamagePushTests
{
    // The standing hull the push's force divides by (player.cpp:1622-1638):
    // 32 x 32 x 72, so the hull's ratio is one and the force is five units
    // a second of damage, capped at a thousand.
    private static readonly Vector HullMin = new(-16f, -16f, 0f);
    private static readonly Vector HullMax = new(16f, 16f, 72f);

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    /// <summary>
    /// The player's damage path over the rig: the real <see cref="GameRules"/>
    /// carrying TF's rule chain (<see cref="CombatRig.Rules"/>) and modifier
    /// (<see cref="CombatRig.Layer"/>), a walking move record for the push's
    /// gate, and the rig's own engine answering the entity questions. Friendly
    /// fire is held on so the core's own answers defer to the chain, as a
    /// two-team match does.
    /// </summary>
    private static SsPlayer Player(CombatRig rig)
    {
        var convars = ConVarRegistry.For(UnattachedConsole());
        convars.RegisterAll(typeof(GameRules).Assembly);
        convars.Declared<MpFriendlyFireConVar>().Bind(-1, "mp_friendlyfire", "1");

        var host = Fake().Returns("Host.ActiveSide", Side.Server);
        var rules = new GameRules(new RecordingLog(), convars.Holder<GameRulesConVars>(), host, rig.Engine, rig.Engine,
                                  new Scoreboard(host, new PlayerDisconnectEvent(), new RecordingLog()),
                                  new PlayerDeathEvent(),
                                  damageRules: new ListHooks<IDamageRule>(rig.Rules),
                                  damageModifiers: new ListHooks<IDamageModifier>(rig.Layer));

        var moves = new ServerPlayerMoves();
        moves.For(CombatRig.Victim);

        var player = new SsPlayer(rig.Engine, rig.Engine, Impacts(rig.Engine), Fluid(), InfiniteAux(), PrintDamage(),
                                  messages: rig.Engine, inflictors: rig.Inflictors, rules: rules, moves: moves,
                                  events: rig.Engine, entities: rig.Engine)
        { Handle = CombatRig.Victim };
        player.BindAreas(Areas(rig.Engine));
        player.State.BindLocal();

        rig.Engine.Returns("Entities.GetBounds", (HullMin, HullMax))
                  .ReturnsEachCall("Entities.GetSolidFlags", () => default(SolidFlags))
                  .Returns("GameEvents.EventCreate", new IntPtr(7))
                  .Returns("GameEvents.EventFire", true);
        return player;
    }

    /// <summary>
    /// A rocket blast: blast damage from the attacker (the rig puts it at
    /// <see cref="CombatRig.AttackerAt"/>, five hundred twelve units along +x
    /// at eye level). The returned number is what the shell subtracts from the
    /// player's health.
    /// </summary>
    [Fact]
    public void An_ubered_victim_of_a_rocket_blast_keeps_its_health_and_keeps_the_push()
    {
        // The number the modify rules answer for this exact hit — the blast's
        // own through the layer, once.
        var observed = new CombatRig();
        float modified = observed.Hit(40f, DamageType.Blast);

        var rig = new CombatRig();
        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.Invulnerable));
        var player = Player(rig);

        int taken = player.OnDamagedAt(CombatRig.Attacker, 40f, DamageType.Blast, default, default);

        Assert.True(taken == 0, $"the übered victim keeps its health: the shell subtracts {taken}, not {modified}");

        // The hit's events ran: the hurt path reached the game event.
        Assert.True(rig.Engine.Called("GameEvents.EventCreate"), rig.Engine.Describe());

        // And the push landed with the hit's numbers: away from the attacker's
        // centre, five units a second of the modified damage, the rig's hull
        // making the hull ratio exactly one.
        var call = Assert.Single(rig.Engine.AllOf("Entities.ApplyVelocityImpulse"));
        Assert.Equal(CombatRig.Victim, call.ArgAs<EntityHandle>(0));
        Assert.False(call.ArgAs<bool>(1 + 1));
        Vector impulse = call.ArgAs<Vector>(1);

        float force = MathF.Min(modified * 5f, 1000f);
        float length = MathF.Sqrt(512f * 512f + 10f * 10f);   // towards: AttackerAt minus the 10-unit lift, from the origin
        Assert.Equal(-force * (512f / length), impulse.X, 2);
        Assert.Equal(0f, impulse.Y, 2);
        Assert.Equal(force * (10f / length), impulse.Z, 2);   // the 10-unit lift negates with the rest
    }

    /// <summary>
    /// Without the über the arm answers nothing differently: the hit lands
    /// whole (the shell subtracts the modified damage) and pushes the same.
    /// </summary>
    [Fact]
    public void A_non_ubered_victim_of_the_same_blast_is_unchanged()
    {
        var observed = new CombatRig();
        float modified = observed.Hit(40f, DamageType.Blast);

        var rig = new CombatRig();
        var player = Player(rig);

        int taken = player.OnDamagedAt(CombatRig.Attacker, 40f, DamageType.Blast, default, default);

        Assert.True(taken == (int)modified, $"the hit lands whole, as before the events-only arm: taken {taken}, modified {modified}");
        Assert.True(rig.Engine.Called("Entities.ApplyVelocityImpulse"), rig.Engine.Describe());
        Assert.True(rig.Engine.Called("GameEvents.EventCreate"), rig.Engine.Describe());
    }
}
