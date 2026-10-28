using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// A player's +USE: CHL2_Player::PlayerUse (hl2_player.cpp:2826) over
/// CBasePlayer::FindUseEntity (baseplayer_shared.cpp:1067), and the object
/// caps that say what may be used (FCAP_*, CBaseEntity::ObjectCaps and the
/// classes' own). Found side by side with stock (2026-10-26): a +USE aimed at
/// ss_sandbox's health_button fired nothing here, because nothing on this
/// side read the key but the ladders.
/// </summary>
public class PlayerUseTests
{
    private static readonly EntityHandle Player = new(0x0001_0003);
    private static readonly EntityHandle Target = new(0x0002_0040);
    private static readonly EntityHandle Part = new(0x0003_0041);

    private const int InUse = 1 << 5;

    //-------------------------------------------------------------------------
    // The object caps
    //-------------------------------------------------------------------------

    private static UseCaps CapsOf(string className, int spawnFlags, EntityHandle parent = default)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncButton).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.GetSpawnFlags", spawnFlags)
              .ReturnsEachCall("Entities.GetParent",
                  () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Target ? parent : EntityHandle.None);
        registry.Spawn(Target, className);
        return registry.Get(Target)!.ObjectCaps;
    }

    [Fact]
    public void A_button_whose_use_activates_it_is_used_on_a_press_and_found_in_the_radius()
    {
        Assert.Equal(UseCaps.ImpulseUse | UseCaps.UseInRadius, CapsOf(EntityNames.FuncButton, 1024));
        Assert.Equal(UseCaps.None, CapsOf(EntityNames.FuncButton, 0));
        Assert.Equal(UseCaps.ImpulseUse | UseCaps.UseInRadius, CapsOf(EntityNames.FuncRotButton, 1024));
    }

    [Fact]
    public void A_door_is_used_only_when_its_map_says_use_opens_it()
    {
        Assert.Equal(UseCaps.ImpulseUse | UseCaps.UseInRadius, CapsOf(EntityNames.FuncDoor, 256));
        Assert.Equal(UseCaps.None, CapsOf(EntityNames.FuncDoor, 0));
        Assert.Equal(UseCaps.ImpulseUse | UseCaps.UseInRadius, CapsOf(EntityNames.FuncDoorRotating, 256));
    }

    [Fact]
    public void A_prop_door_is_used_unless_it_ignores_use()
    {
        Assert.Equal(UseCaps.ImpulseUse | UseCaps.UseInRadius, CapsOf(EntityNames.PropDoorRotating, 0));
        Assert.Equal(UseCaps.None, CapsOf(EntityNames.PropDoorRotating, 32768));
    }

    [Fact]
    public void A_func_brush_is_usable_unless_it_ignores_player_use()
    {
        Assert.Equal(UseCaps.ImpulseUse, CapsOf(EntityNames.FuncBrush, 0));
        Assert.Equal(UseCaps.None, CapsOf(EntityNames.FuncBrush, 2));
    }

    [Fact]
    public void A_child_answers_its_parents_usable_bits_but_not_how_the_parent_is_found()
    {
        // The parent: a button that use activates (impulse, radius). The child
        // keeps the impulse and drops the radius (CBaseEntity::ObjectCaps).
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncButton).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Entities.IsValid", true)
              .ReturnsEachCall("Entities.GetSpawnFlags",
                  () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Target ? 1024 : 2)
              .ReturnsEachCall("Entities.GetParent",
                  () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Part ? Target : EntityHandle.None);
        registry.Spawn(Target, EntityNames.FuncButton);
        registry.Spawn(Part, EntityNames.FuncBrush);

        Assert.Equal(UseCaps.ImpulseUse | UseCaps.UseInRadius, registry.Get(Target)!.ObjectCaps);
        Assert.Equal(UseCaps.ImpulseUse, registry.Get(Part)!.ObjectCaps);
    }

    //-------------------------------------------------------------------------
    // PlayerUse
    //-------------------------------------------------------------------------

    private sealed class Usable : ScriptedEntity
    {
        public UseCaps Caps;
        public override UseCaps ObjectCaps => Caps;
    }

    private sealed class Directory : IEntityDirectory
    {
        public readonly Dictionary<EntityHandle, ScriptedEntity> Entities = [];
        public ScriptedEntity? Get(EntityHandle handle) => Entities.GetValueOrDefault(handle);
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => false;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    private sealed class Aimed : IUseTargetFinder
    {
        public EntityHandle At = EntityHandle.None;
        public int Asked;
        public EntityHandle Find(EntityHandle player)
        {
            Asked++;
            return At;
        }
    }

    private static (PlayerUse Use, RecordingEngine Engine, SsPlayer Player, Aimed Aim, Usable Target) UseRig(UseCaps caps)
    {
        var engine = Fake();
        engine.Strict = false;
        engine.Returns("Entities.IsValid", true);
        var player = new SsPlayer(engine, engine, Impacts(engine), Fluid(), messages: engine, moves: new ServerPlayerMoves(),
                                  sound: engine, damage: engine)
        {
            Handle = Player,
        };
        player.State.BindLocal();
        player.State.Alive = true;
        var target = new Usable { Handle = Target, Caps = caps };
        var directory = new Directory();
        directory.Entities[Player] = player;
        directory.Entities[Target] = target;
        var aim = new Aimed { At = Target };
        return (new PlayerUse(aim, engine, engine, engine, directory), engine, player, aim, target);
    }

    private static int Uses(RecordingEngine engine)
        => engine.AllOf("Entities.FireInput").Count(c => (string)c.Args[1]! == "Use"
                                                      && c.ArgAs<EntityHandle>(0) == Target
                                                      && c.ArgAs<EntityHandle>(3) == Player);

    private static string[] Sounds(RecordingEngine engine)
        => engine.AllOf("Sound.EntityEmitSound").Select(c => (string)c.Args[1]!).ToArray();

    [Fact]
    public void A_press_on_a_button_uses_it_once_with_the_player_as_activator()
    {
        var (use, engine, _, _, _) = UseRig(UseCaps.ImpulseUse);

        use.BeforeMove(Player, InUse);

        Assert.Equal(1, Uses(engine));
        Assert.Equal([PlayerUse.UseSound], Sounds(engine));
        Assert.True(engine.Called("PlayerMovement.SwallowUse"), "the press that used something is debounced for the move");

        // Holding the key is not pressing it again.
        engine.Clear();
        use.BeforeMove(Player, InUse);
        Assert.Equal(0, Uses(engine));
        Assert.Empty(Sounds(engine));
    }

    [Fact]
    public void A_held_key_uses_a_continuous_target_every_command()
    {
        var (use, engine, _, _, _) = UseRig(UseCaps.ContinuousUse);

        use.BeforeMove(Player, InUse);
        use.BeforeMove(Player, InUse);
        use.BeforeMove(Player, InUse);

        Assert.Equal(3, Uses(engine));
        Assert.Equal([PlayerUse.UseSound], Sounds(engine));
    }

    [Fact]
    public void An_on_off_target_is_used_on_the_press_and_again_on_the_release()
    {
        var (use, engine, _, _, _) = UseRig(UseCaps.OnOffUse);

        use.BeforeMove(Player, InUse);
        use.BeforeMove(Player, InUse);
        use.BeforeMove(Player, 0);

        Assert.Equal(2, Uses(engine));
    }

    [Fact]
    public void A_press_on_nothing_queues_the_deny_sound_for_after_the_move()
    {
        var (use, engine, _, aim, _) = UseRig(UseCaps.ImpulseUse);
        aim.At = EntityHandle.None;

        use.BeforeMove(Player, InUse);
        Assert.True(engine.Called("PlayerMovement.QueueUseDenySound"));
        Assert.Empty(Sounds(engine));
        Assert.False(engine.Called("PlayerMovement.SwallowUse"), "nothing used, nothing debounced");

        engine.Returns("PlayerMovement.TakeUseDenySound", true);
        use.AfterMove(Player);
        Assert.Equal([PlayerUse.DenySound], Sounds(engine));
    }

    [Fact]
    public void The_deny_sound_waits_for_a_live_player()
    {
        var (use, engine, player, _, _) = UseRig(UseCaps.ImpulseUse);
        engine.Returns("PlayerMovement.TakeUseDenySound", true);
        player.State.Alive = false;

        use.AfterMove(Player);

        Assert.False(engine.Called("PlayerMovement.TakeUseDenySound"), "PostThink runs ItemPostFrame only for a live player");
        Assert.Empty(Sounds(engine));
    }

    [Fact]
    public void A_press_on_a_ladder_uses_nothing()
    {
        var (use, engine, player, aim, _) = UseRig(UseCaps.ImpulseUse);
        player.State.MoveType = (byte)GameMovement.MoveTypeLadder;

        use.BeforeMove(Player, InUse);

        Assert.Equal(0, aim.Asked);
        Assert.Equal(0, Uses(engine));
    }

    [Fact]
    public void A_target_that_is_not_usable_is_not_used_but_spares_the_deny()
    {
        // A target the finder returned answers its caps now; stock tests
        // them on the press as here, and an entity found but not usable this
        // way plays the use sound and is not used.
        var (use, engine, _, _, _) = UseRig(UseCaps.ContinuousUse);

        use.BeforeMove(Player, 0);
        Assert.Equal(0, Uses(engine));
        Assert.False(engine.Called("PlayerMovement.QueueUseDenySound"));
    }

    //-------------------------------------------------------------------------
    // FindUseEntity
    //-------------------------------------------------------------------------

    private static (PlayerUseFinder Finder, RecordingEngine Engine, Directory Directory) FinderRig()
    {
        var engine = Fake();
        engine.Strict = false;
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f))
              .Returns("Entities.GetEyeAngles", Vector.Zero)
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)))
              .Returns("Entities.GetParent", EntityHandle.None);
        var directory = new Directory();
        directory.Entities[Target] = new Usable { Handle = Target, Caps = UseCaps.ImpulseUse };
        return (new PlayerUseFinder(engine, engine, directory, ConVarRegistry.For(UnattachedConsole()), new RecordingLog()),
                engine, directory);
    }

    [Fact]
    public void What_is_straight_ahead_and_near_is_taken_at_once()
    {
        var (finder, engine, _) = FinderRig();
        engine.Returns("WorldQuery.EntityTraceHull",
                       new EntityHullTrace(new Vector(40f, 0f, 64f), false, false, Target, 0.04f));

        Assert.Equal(Target, finder.Find(Player));
        Assert.Single(engine.AllOf("WorldQuery.EntityTraceHull"));
        var line = engine.Last("WorldQuery.EntityTraceHull")!.Value;
        Assert.Equal(new Vector(PlayerUseFinder.LineLength, 0f, 64f), line.ArgAs<Vector>(2));
        Assert.Equal(PlayerUseFinder.UseableContents, line.ArgAs<uint>(5));
    }

    [Fact]
    public void What_is_straight_ahead_but_beyond_the_radius_is_not()
    {
        var (finder, engine, _) = FinderRig();
        engine.Returns("WorldQuery.EntityTraceHull",
                       new EntityHullTrace(new Vector(200f, 0f, 64f), false, false, Target, 0.2f));

        Assert.Equal(EntityHandle.None, finder.Find(Player));
        Assert.Equal(8, engine.AllOf("WorldQuery.EntityTraceHull").Count());
    }

    [Fact]
    public void An_unusable_part_hands_the_use_to_its_usable_parent()
    {
        var (finder, engine, directory) = FinderRig();
        directory.Entities[Part] = new Usable { Handle = Part, Caps = UseCaps.None };
        engine.ReturnsEachCall("Entities.GetParent",
                  () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Part ? Target : EntityHandle.None)
              .Returns("WorldQuery.EntityTraceHull",
                       new EntityHullTrace(new Vector(40f, 0f, 64f), false, false, Part, 0.04f));

        Assert.Equal(Target, finder.Find(Player));
    }

    private static (PlayerUseFinder Finder, RecordingEngine Engine) RadiusRig(Vector at, EntityHullTrace occlusion)
    {
        var (finder, engine, directory) = FinderRig();
        directory.Entities[Target] = new Usable { Handle = Target, Caps = UseCaps.ImpulseUse | UseCaps.UseInRadius };
        // The sweeps find nothing; the occlusion line is the last trace asked.
        int traces = 0;
        engine.ReturnsEachCall("WorldQuery.EntityTraceHull",
                  () => ++traces <= 8 ? new EntityHullTrace(Vector.Zero, false, false, EntityHandle.None, 1f) : occlusion)
              .FillsSphere(Target)
              .Returns("Entities.GetSolidType", SolidType.BoundingBox)
              .ReturnsEachCall("Entities.GetOrigin",
                  () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Target ? at : Vector.Zero)
              .ReturnsEachCall("Entities.GetBounds",
                  () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Target
                      ? (new Vector(-1f, -1f, -1f), new Vector(1f, 1f, 1f))
                      : (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)));
        return (finder, engine);
    }

    [Fact]
    public void A_radius_target_in_front_and_in_sight_is_found()
    {
        var (finder, _) = RadiusRig(new Vector(60f, 10f, 64f), new EntityHullTrace(Vector.Zero, false, false, EntityHandle.None, 1f));
        Assert.Equal(Target, finder.Find(Player));
    }

    [Fact]
    public void A_radius_target_off_to_the_side_is_not()
    {
        // 60 units ahead and 60 aside: 45 degrees off the view, past the 0.8 cone.
        var (finder, _) = RadiusRig(new Vector(60f, 60f, 64f), new EntityHullTrace(Vector.Zero, false, false, EntityHandle.None, 1f));
        Assert.Equal(EntityHandle.None, finder.Find(Player));
    }

    [Fact]
    public void A_radius_target_behind_glass_is_not()
    {
        var glass = new EntityHandle(0x0004_0050);
        var (finder, _) = RadiusRig(new Vector(60f, 10f, 64f), new EntityHullTrace(Vector.Zero, false, false, glass, 0.5f));
        Assert.Equal(EntityHandle.None, finder.Find(Player));
    }

    [Fact]
    public void The_height_test_measures_from_the_raised_centre_plus_the_box()
    {
        Assert.Equal(0f, PlayerUseFinder.IntervalDistance(50f, 36f, 108f));
        Assert.Equal(6f, PlayerUseFinder.IntervalDistance(30f, 36f, 108f));
        Assert.Equal(2f, PlayerUseFinder.IntervalDistance(110f, 108f, 36f));
    }

    [Fact]
    public void The_view_line_runs_to_the_point_the_direction_names_as_stocks_does()
    {
        // Stock passes the view DIRECTION as the line's second point: from an
        // eye at (0, 100, 0) looking along +x, the line runs to (1, 0, 0), so
        // a point dead ahead is not on it.
        float d = PlayerUseFinder.DistanceToLine(new Vector(50f, 100f, 0f), new Vector(0f, 100f, 0f), new Vector(1f, 0f, 0f));
        Assert.Equal(49.9975f, d, 3);
    }

    [Fact]
    public void The_nearest_point_turns_with_an_entity_space_box_and_not_with_a_world_aligned_one()
    {
        var (finder, engine, _) = FinderRig();
        engine.Returns("Entities.GetOrigin", Vector.Zero)
              .Returns("Entities.GetBounds", (new Vector(-10f, -1f, -1f), new Vector(10f, 1f, 1f)))
              .Returns("Entities.GetAngles", new Vector(0f, 90f, 0f))
              .Returns("Entities.GetSolidType", SolidType.VPhysics);

        Vector turned = finder.NearestPoint(Target, new Vector(0f, 50f, 0f));
        Assert.Equal(0f, turned.X, 3);
        Assert.Equal(10f, turned.Y, 3);

        engine.Returns("Entities.GetSolidType", SolidType.BoundingBox);
        Vector aligned = finder.NearestPoint(Target, new Vector(0f, 50f, 0f));
        Assert.Equal(new Vector(0f, 1f, 0f), aligned);
    }
}
