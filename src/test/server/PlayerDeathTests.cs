using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The three generic death hooks a mod builds on (plan_descent_death §4,
/// core): it hears of a death with the body it left (<see cref="IPlayerDeathHook"/>),
/// it can keep a dead player dead (<see cref="IRespawnGate"/>), and it can
/// keep a body past its lifetime and the sweep (<see cref="Corpses.Keep"/>).
/// </summary>
public class PlayerDeathTests
{
    private const string Model = "models/humans/group03/male_01.mdl";
    private const int PlayerIndex = 3;

    private static readonly EntityHandle Player = new(7u);
    private static readonly EntityHandle Attacker = new(8u);
    private static readonly EntityHandle Body = new(99u);
    private static readonly EntityHandle SecondBody = new(100u);
    private static readonly Vector DiedAt = new(10f, 20f, 30f);

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Bodies(params ScriptedEntity[] bodies) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle handle) => bodies.FirstOrDefault(b => b.Handle == handle);
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => Array.Empty<ScriptedEntity>();
        public bool IsRegistered(string className) => className == EntityNames.SsRagdoll;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => Array.Empty<ScriptedClassInfo>();
    }

    private static RecordingEngine Dying()
    {
        var engine = Fake();
        engine.Returns("EntityRender.GetModelName", Model);
        engine.Returns("Entities.GetOrigin", DiedAt);
        engine.Returns("Entities.GetAngles", default(Vector));
        engine.Returns("Entities.GetVelocity", default(Vector));
        engine.Returns("Entities.Create", Body);
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Entities.GetIndex", PlayerIndex);
        // The player_death notice PlayerDeaths fires (stock order, before the
        // record). The scorer branch of DeathNotice.Fire: a real attacker
        // reads a userid and the active weapon; the event itself stops at a
        // zero handle, which is what the double answers by default.
        engine.Returns("Entities.IsPlayer", true);
        engine.Returns("Players.GetUserId", 0);
        engine.Returns("Players.GetActiveWeapon", string.Empty);
        engine.Returns("GameEvents.EventCreate", IntPtr.Zero);
        return engine;
    }

    private static SsRagdoll Ragdoll(RecordingEngine engine, EntityHandle handle)
    {
        var body = new SsRagdoll(engine) { Handle = handle };
        body.BindEngine(engine);
        body.State.BindLocal();
        return body;
    }

    // ---- IPlayerDeathHook --------------------------------------------------

    private sealed class Heard : IPlayerDeathHook
    {
        public PlayerDeath? Death;
        public int DeathsOnRecord = -1;
        public Func<int>? ReadDeaths;
        public void OnPlayerDied(PlayerDeath death)
        {
            Death = death;
            DeathsOnRecord = ReadDeaths?.Invoke() ?? -1;
        }
    }

    private static (PlayerDeaths Deaths, Players Players, Heard Hook) Deaths(RecordingEngine engine)
    {
        var corpses = new Corpses(new RecordingLog(), engine, engine, new Bodies(Ragdoll(engine, Body)), engine);
        var players = new Players(new RecordingLog(), engine, engine, engine, NoButtons) { DropWeaponOnDeath = false };
        var hook = new Heard { ReadDeaths = () => players[Player].Deaths };
        return (new PlayerDeaths(new RecordingLog(), engine, corpses, players, engine, new ListHooks<IPlayerDeathHook>(hook),
                                 new DeathNotice(engine, engine, engine, new DamageInflictors())), players, hook);
    }

    [Fact]
    public void A_death_hook_hears_who_died_where_and_the_body_they_left()
    {
        var engine = Dying();
        var (deaths, _, hook) = Deaths(engine);

        deaths.OnKilled(Player, Attacker);

        Assert.Equal(new PlayerDeath(Player, PlayerIndex, Attacker, DiedAt, Body), hook.Death);
    }

    [Fact]
    public void The_hook_runs_after_the_death_is_on_the_record()
    {
        var engine = Dying();
        var (deaths, _, hook) = Deaths(engine);

        deaths.OnKilled(Player, Attacker);

        Assert.Equal(1, hook.DeathsOnRecord);
    }

    [Fact]
    public void Where_they_died_is_read_before_the_body_is_made()
    {
        var engine = Dying();
        var (deaths, _, _) = Deaths(engine);

        deaths.OnKilled(Player, Attacker);

        // TWO reads before the body is created: this service's, and the one
        // Corpses.Make takes for the body itself. One would mean this service
        // read the origin only after the body (and any map logic it ran).
        var members = engine.Calls.Select(c => c.Member).ToList();
        int create = members.IndexOf("Entities.Create");
        Assert.True(create > 0, "no body was created: the stimulus did not reach Make");
        Assert.Equal(2, members.Take(create).Count(m => m == "Entities.GetOrigin"));
    }

    // ---- IRespawnGate -------------------------------------------------------

    private sealed class Gate(bool allow) : IRespawnGate
    {
        public int Asked;
        public int AskedIndex = -1;
        public bool MayRespawn(EntityHandle player, int entIndex)
        {
            Asked++;
            AskedIndex = entIndex;
            return allow;
        }
    }

    private sealed class ThrowingGate : IRespawnGate
    {
        public bool MayRespawn(EntityHandle player, int entIndex) => throw new InvalidOperationException("broken mod");
    }

    /// <summary>
    /// Dead past the delay and asking to come back (every button held): the
    /// moment the stock cycle respawns. Returns how many respawns reached the
    /// engine.
    /// </summary>
    private static int RespawnsAfterTheDelay(params IRespawnGate[] gates)
    {
        var engine = Fake();
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Entities.GetIndex", PlayerIndex);
        engine.Returns("Players.IsAlive", false);

        var players = new Players(new RecordingLog(), engine, engine, engine, NoButtons,
                                  gates.Length == 0 ? null : new ListHooks<IRespawnGate>(gates))
        {
            DropWeaponOnDeath = false,   // the weapon is not this file's subject
        };

        players.OnKilled(Player, EntityHandle.None, now: 0f);
        players.Think(1f, _ => true);                       // starts watching
        players.Think(players.RespawnDelay + 1f, _ => true); // past the delay, asking

        return engine.Calls.Count(c => c.Member == "Players.Respawn");
    }

    [Fact]
    public void Without_gates_a_dead_player_respawns_as_before()
    {
        Assert.Equal(1, RespawnsAfterTheDelay());
    }

    [Fact]
    public void A_gate_that_allows_lets_the_player_respawn()
    {
        var gate = new Gate(allow: true);

        Assert.Equal(1, RespawnsAfterTheDelay(gate));
        Assert.Equal(PlayerIndex, gate.AskedIndex);
    }

    [Fact]
    public void A_gate_that_refuses_keeps_the_player_dead_however_long_they_ask()
    {
        var gate = new Gate(allow: false);

        Assert.Equal(0, RespawnsAfterTheDelay(gate));
        Assert.True(gate.Asked > 0, "the gate was never asked: the stimulus did not reach it");
    }

    [Fact]
    public void Any_refusal_vetoes_even_when_another_gate_allows()
    {
        Assert.Equal(0, RespawnsAfterTheDelay(new Gate(allow: true), new Gate(allow: false)));
    }

    [Fact]
    public void A_gate_that_throws_does_not_trap_the_player()
    {
        Assert.Equal(1, RespawnsAfterTheDelay(new ThrowingGate()));
    }

    // ---- Corpses.Keep -------------------------------------------------------

    [Fact]
    public void A_kept_body_outlives_its_lifetime()
    {
        var engine = Dying();
        var body = Ragdoll(engine, Body);
        var corpses = new Corpses(new RecordingLog(), engine, engine, new Bodies(body), engine);

        Assert.Equal(Body, corpses.Make(Player));
        Assert.True(corpses.Keep(Body));

        body.OnThink();   // the lifetime is up

        Assert.False(body.Gone);
    }

    [Fact]
    public void A_body_that_is_not_kept_still_goes_at_its_lifetime()
    {
        var engine = Dying();
        var body = Ragdoll(engine, Body);
        var corpses = new Corpses(new RecordingLog(), engine, engine, new Bodies(body), engine);

        Assert.Equal(Body, corpses.Make(Player));

        body.OnThink();

        Assert.True(body.Gone);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_kept_body_is_out_of_the_sweep(bool keep, bool firstRemoved)
    {
        var engine = Dying();
        var corpses = new Corpses(new RecordingLog(), engine, engine, new Bodies(Ragdoll(engine, Body), Ragdoll(engine, SecondBody)), engine)
        {
            Limit = 1,
        };

        Assert.Equal(Body, corpses.Make(Player));
        if (keep)
            Assert.True(corpses.Keep(Body));

        engine.Returns("Entities.Create", SecondBody);
        Assert.Equal(SecondBody, corpses.Make(Player));

        bool removed = engine.Calls.Any(c => c.Member == "Entities.Remove" && Equals(c.Arg(0), Body));
        Assert.Equal(firstRemoved, removed);
    }

    [Fact]
    public void Keeping_a_body_this_service_does_not_hold_is_refused()
    {
        var engine = Dying();
        var corpses = new Corpses(new RecordingLog(), engine, engine, new Bodies(), engine);

        Assert.False(corpses.Keep(Body));
    }

    [Fact]
    public void A_kept_body_stays_kept_across_a_hot_reload()
    {
        var engine = Dying();
        var before = Ragdoll(engine, Body);
        before.Kept = true;

        var record = new SavedEntity(Body.Value, EntityNames.SsRagdoll, string.Empty);
        before.SaveState(record);

        var after = Ragdoll(engine, Body);
        after.LoadState(record);

        Assert.True(after.Kept);
        after.OnThink();
        Assert.False(after.Gone);
    }
}
