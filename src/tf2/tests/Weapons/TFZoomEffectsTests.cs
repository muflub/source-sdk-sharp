using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Entities;
using TF2.Server.Weapons;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <see cref="TFZoomEffects"/>: the scope's server half — the stock zoom FOV
/// in, the player's own back out, the zoom-in clock the headshot reads, and
/// the aim dot made and taken with the zoom (stock's CreateSniperDot /
/// DestroySniperDot, tf_weapon_sniperrifle.cpp:922-959).
/// </summary>
public sealed class TFZoomEffectsTests
{
    private const string Rifle = "tf_weapon_sniperrifle";
    private const int Owner = TFWeaponWorld.Owner;

    private static readonly EntityHandle DotHandle = new(0x0004_0021u);
    private static readonly EntityHandle NextDotHandle = new(0x0004_0022u);
    private static readonly EntityHandle OwnerHandle = new(0x0001_0003u);

    private sealed class DotDirectory(SniperDot dot, params EntityHandle[] handles) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h) => handles.Contains(h) ? dot : null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => true;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    private static (TFZoomEffects Effects, TFZoomTimes Times, TFWeaponWorld World, WeaponAttackContext Attack,
                    RecordingEngine Engine, SniperDot Dot) Scope(float now = 10f)
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Rifle, clip: -1, reserve: 25);
        var times = new TFZoomTimes();
        var engine = TestServices.Fake();
        var dot = new SniperDot(engine, engine);
        dot.BindAreas(TestServices.Areas(engine));
        dot.NetStates = NetStateBinding.AttachLocal(dot, DotHandle, new NetStateFields(new RecordingLog()));
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.Create", DotHandle)
              .Returns("Players.GetHandle", OwnerHandle)
              .Returns("Entities.GetIndex", 0)
              .Returns("Players.GetTeam", (int)TFTeam.Unassigned)
              .Returns("Entities.SetOwner", true)
              .Returns("Players.IsAlive", true);
        var effects = new TFZoomEffects(times, engine, engine, new DotDirectory(dot, DotHandle, NextDotHandle), world.Log);
        return (effects, times, world, world.Attack(w, vm, now), engine, dot);
    }

    // The fov words are the behaviour's predicted write (both sides stamp them
    // inside the command — stock's SetFOV behind CTFSniperRifle::ZoomIn/ZoomOut),
    // so the seam asks for none itself: it carries what only the server does.
    // The stamped values themselves the replay's facts hold
    // (TF2ClientPredictionTests.A_replayed_zoom_stamps_the_fov_words...).

    [Fact]
    public void Zooming_in_stamps_the_zoom_in_clock_and_writes_no_fov_word_itself()
    {
        var (effects, times, world, attack, _, _) = Scope(10f);

        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: true);

        Assert.Empty(world.Arms.Fovs);
        Assert.Equal(10f, times.ZoomedAt(Owner));
    }

    [Fact]
    public void Zooming_out_clears_the_stamp_and_writes_no_fov_word_either()
    {
        var (effects, times, world, attack, _, _) = Scope(10f);
        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: true);

        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: false);

        Assert.Empty(world.Arms.Fovs);
        Assert.Equal(0f, times.ZoomedAt(Owner));
    }

    [Fact]
    public void A_player_who_never_zoomed_has_no_stamp()
    {
        var (_, times, _, _, _, _) = Scope();

        Assert.Equal(0f, times.ZoomedAt(Owner));
    }

    [Fact]
    public void A_second_zoom_in_restamps_the_clock()
    {
        var (effects, times, _, attack, _, dot) = Scope(10f);
        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: true);
        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: false);

        var later = attack with { Now = 12f };
        effects.Zoom(in later, WeaponFixtures.Parse(Rifle), zoomed: true);

        Assert.Equal(12f, times.ZoomedAt(Owner));
        Assert.Equal(12f, dot.ChargeStartTime);
    }

    [Fact]
    public void The_dots_charge_clock_starts_at_the_zoom_that_made_it()
    {
        var (effects, _, _, attack, _, dot) = Scope(10f);

        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: true);

        Assert.Equal(10f, dot.ChargeStartTime);
    }

    // ---- the aim dot ---------------------------------------------------------

    [Fact]
    public void Zooming_in_creates_the_owners_dot()
    {
        var (effects, _, _, attack, engine, dot) = Scope(10f);

        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: true);

        Assert.Equal(SniperDot.MapClass,
                     engine.Calls.Single(c => c.Member == "Entities.Create").ArgAs<string>(0));
        Assert.Equal(OwnerHandle, dot.Owner);
        Assert.Equal(1, engine.Calls.Count(c => c.Member == "Entities.Spawn"));
    }

    [Fact]
    public void Zooming_out_destroys_the_dot()
    {
        var (effects, _, _, attack, engine, _) = Scope(10f);
        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: true);

        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: false);

        Assert.Equal(DotHandle,
                     engine.Calls.Single(c => c.Member == "Entities.Remove").ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void A_zoom_in_while_the_dot_lives_makes_no_second_dot()
    {
        var (effects, _, _, attack, engine, _) = Scope(10f);
        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: true);

        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: true);

        Assert.Equal(1, engine.Calls.Count(c => c.Member == "Entities.Create"));
    }

    [Fact]
    public void A_dot_the_owners_death_took_is_replaced_on_the_next_zoom_in()
    {
        var (effects, _, _, attack, engine, dot) = Scope(10f);
        bool firstGone = false;
        engine.ReturnsEachCall("Entities.IsValid",
                               () => !firstGone || engine.Calls[^1].ArgAs<EntityHandle>(0) != DotHandle);
        var handles = new[] { DotHandle, NextDotHandle };
        int created = 0;
        engine.ReturnsEachCall("Entities.Create", () => handles[Math.Min(created++, handles.Length - 1)]);
        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: true);

        // The dot's own think removed it when the owner died; the effect's
        // handle is stale and must not be reused.
        firstGone = true;
        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: false);
        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: true);

        Assert.Equal(2, created);
        Assert.Equal(OwnerHandle, dot.Owner);
        Assert.Equal(2, engine.Calls.Count(c => c.Member == "Entities.Spawn"));
    }

    [Fact]
    public void Zooming_out_when_the_dot_is_already_gone_removes_nothing()
    {
        var (effects, _, _, attack, engine, _) = Scope(10f);
        bool firstGone = false;
        engine.ReturnsEachCall("Entities.IsValid",
                               () => !firstGone || engine.Calls[^1].ArgAs<EntityHandle>(0) != DotHandle);
        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: true);
        firstGone = true;

        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: false);

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.Remove");
    }

    // The regression the live gate caught: the directory learns the instance
    // from the spawn's callback, so a dress read before the Spawn found
    // nothing and the dot was never spawned at all -- created, inert, gone.
    [Fact]
    public void The_dot_is_dressed_only_once_the_spawn_has_made_it()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Rifle, clip: -1, reserve: 25);
        var times = new TFZoomTimes();
        var engine = TestServices.Fake();
        var dot = new SniperDot(engine, engine);
        dot.BindAreas(TestServices.Areas(engine));
        dot.NetStates = NetStateBinding.AttachLocal(dot, DotHandle, new NetStateFields(new RecordingLog()));
        var directory = new SpawnGatedDirectory(engine, dot, DotHandle);
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.Create", DotHandle)
              .Returns("Players.GetHandle", OwnerHandle)
              .Returns("Entities.GetIndex", 0)
              .Returns("Players.GetTeam", (int)TFTeam.Unassigned)
              .Returns("Entities.SetOwner", true)
              .Returns("Players.IsAlive", true);
        var effects = new TFZoomEffects(times, engine, engine, directory, world.Log);
        var attack = world.Attack(w, vm, 10f);

        effects.Zoom(in attack, WeaponFixtures.Parse(Rifle), zoomed: true);

        Assert.Equal(OwnerHandle, dot.Owner);
        Assert.Equal(10f, dot.ChargeStartTime);
        Assert.Equal(1, engine.Calls.Count(c => c.Member == "Entities.Spawn"));
    }

    /// <summary>A directory that answers only once a spawn has gone through: the
    /// live shape, where the spawn's callback is what binds the instance. The
    /// gate reads the engine's own call log, the same record the facts assert on.</summary>
    private sealed class SpawnGatedDirectory(RecordingEngine engine, SniperDot dot, EntityHandle handle) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h)
            => h == handle && engine.Calls.Any(c => c.Member == "Entities.Spawn"
                                                 && c.ArgAs<EntityHandle>(0) == h)
                ? dot
                : null;

        public ScriptedEntity? FindByName(string name) => null;

        public IEnumerable<ScriptedEntity> OfClass(string className) => [];

        public bool IsRegistered(string className) => true;

        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }
}
