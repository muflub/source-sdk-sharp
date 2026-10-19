using System;
using System.Collections.Generic;
using System.Linq;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The development console (DevCommands.cs and the command classes beside it) —
/// the forty-three registered server ConCommands that resolved their subject as
/// a CBasePlayer, got NULL, and returned without a word.
///
/// <para>
/// WHAT CAN REGRESS, and what each group below pins:
/// </para>
/// <list type="bullet">
/// <item>the sv_cheats gate, in BOTH directions — it has been put wrong twice
/// on this project inside a week, once too early (ordinary commands swallowed)
/// and once absent;</item>
/// <item><c>give</c>'s gate being stock's OWN gate, which is not FCVAR_CHEAT
/// but "single player OR cheats";</item>
/// <item>the picker/name/class selection order, which must be ent_fire's so
/// one written word means one thing across the whole family;</item>
/// <item>the tokeniser, because a dispatch hands managed code ONE STRING and
/// a naive space split loses every quoted argument;</item>
/// <item>that every one of them is REGISTERED — which the predecessor could
/// not check at all, because it kept its own table of verbs beside the
/// registrations rather than being them;</item>
/// <item>and the rule that made all of this expensive: <b>no command is
/// silent</b> — every retirement has a reason and every refusal is a line.</item>
/// </list>
/// </summary>
public class DevCommandsTests
{
    private static readonly EntityHandle Player = new(3u);
    private static readonly EntityHandle Target = new(11u);
    private static readonly EntityHandle Other = new(12u);

    /// <summary>Records what the commands asked the arsenal for.</summary>
    private sealed class RecordingGiver : IWeaponGiver
    {
        public readonly List<string> Weapons = new();
        public readonly List<(string Ammo, int Count)> Ammo = new();
        public bool WeaponTaken = true;
        public int AmmoTaken = 10;

        public bool GiveWeapon(EntityHandle player, string className)
        {
            Weapons.Add(className);
            return WeaponTaken;
        }

        public int GiveAmmo(EntityHandle player, int count, string ammoName)
        {
            Ammo.Add((ammoName, count));
            return AmmoTaken;
        }
    }

    /// <summary>An entity directory that answers from a set of registered names.</summary>
    private sealed class FakeDirectory : IEntityDirectory
    {
        public readonly HashSet<string> Registered = new(StringComparer.OrdinalIgnoreCase);

        public ScriptedEntity? Get(EntityHandle handle) => null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public readonly List<ScriptedClassInfo> Classes = new();

        public bool IsRegistered(string className) => Registered.Contains(className);
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => Classes;
    }

    /// <summary>The networked schema and blob a dump reads, without a game.</summary>
    private sealed class FakeNetState : INetState
    {
        public NetSchemaType? Schema;
        public NetEntityBlob Blob = NetEntityBlob.Missing(0);

        public NetSchemaType? SchemaFor(string className) => Schema;
        public NetEntityBlob Read(int index) => Blob;
    }

    private static FakeNetState _netState = new();

    private static (DevContext Context, RecordingEngine Engine, RecordingGiver Giver, FakeDirectory Directory)
        Rig(bool cheats = true, bool singlePlayer = true)
    {
        var engine = Fake();
        var giver = new RecordingGiver();
        var directory = new FakeDirectory();

        // Where an aim starts: a create measures how far its trace reached.
        engine.Returns("Entities.GetEyePosition", Vector.Zero);

        _netState = new FakeNetState();

        var context = new DevContext(engine, engine, engine, engine, giver, directory, new EntityFactories(), engine, new RecordingLog())
        {
            CheatsEnabled = () => cheats,
            SinglePlayer = () => singlePlayer,
            Issuer = () => Player,
        };

        return (context, engine, giver, directory);
    }

    /// <summary>
    /// One command, built the way the container builds it: whatever its one
    /// constructor asks for, filled from what a test has.
    ///
    /// <para>
    /// BY PARAMETER TYPE, and REFUSING what it cannot supply. What stood here
    /// handed <see cref="_netState"/> to every parameter that was not a
    /// <see cref="DevContext"/> — so the first command to declare a different
    /// dependency (<c>bloodspray</c>, which takes
    /// <see cref="IEngineEffects"/>) would have been handed a net-state to cast
    /// as one. That fails as an unrelated <c>InvalidCastException</c> inside a
    /// reflection loop rather than saying which command and which parameter, so
    /// the unfillable case throws by name instead.
    /// </para>
    /// </summary>
    private static DevCommand Build(Type type, DevContext context, INetState? netState = null)
    {
        var ctor = type.GetConstructors().Single();

        // The RecordingEngine behind the context IS every engine area, so any
        // engine dependency a command declares is already in this test's hands.
        object engine = context.Entities;

        object?[] args = ctor.GetParameters()
            .Select(object? (p) =>
                p.ParameterType == typeof(DevContext) ? context
              : p.ParameterType == typeof(INetState) ? netState ?? _netState
              : p.ParameterType.IsInstanceOfType(engine) ? engine
              : throw new InvalidOperationException(
                    $"DevCommandsTests.Build has nothing to supply for {type.Name}'s " +
                    $"{p.ParameterType.Name} {p.Name}"))
            .ToArray();

        return (DevCommand)ctor.Invoke(args);
    }

    /// <summary>One command, built and run the way the console runs it.</summary>
    private static void Run<T>(DevContext context, string args = "") where T : DevCommand
        => Build(typeof(T), context).Execute(args);

    /// <summary>Stubs the reads Describe makes about an entity.</summary>
    private static RecordingEngine Describable(RecordingEngine engine, string className = "prop_physics")
        => engine.Returns("Entities.IsValid", true)
                 .Returns("Entities.GetName", string.Empty)
                 .Returns("Entities.GetClassName", className)
                 .Returns("Entities.GetIndex", 11)
                 .Returns("Players.GetTeam", 0);   // the team line (tf2 stage 0.5)

    //-------------------------------------------------------------------------
    // The tokeniser. A dispatch hands managed code CCommand::ArgS — the
    // untouched argument text, quotes and all — so this is where an argument
    // with a space in it survives or is lost.
    //-------------------------------------------------------------------------

    [Fact]
    public void APlainLineSplitsIntoItsWords()
    {
        Assert.True(CommandTokens.Split("ent_create prop_physics").SequenceEqual(
                        new[] { "ent_create", "prop_physics" }),
                    "a plain line splits on spaces");

        Assert.True(CommandTokens.Split("   ent_create    prop_physics  ").SequenceEqual(
                        new[] { "ent_create", "prop_physics" }),
                    "and runs of whitespace are one separator, as the engine's tokeniser has it");
    }

    [Fact]
    public void AQuotedArgumentStaysOneWord()
    {
        Assert.True(CommandTokens.Split("ent_keyvalue door rendercolor \"255 0 0\"").SequenceEqual(
                        new[] { "ent_keyvalue", "door", "rendercolor", "255 0 0" }),
                    "a quoted colour is ONE argument — the naive space split that used to " +
                    "stand here turned it into three and set rendercolor to '255'");

        Assert.True(CommandTokens.Split("setmodel \"models/my model.mdl").SequenceEqual(
                        new[] { "setmodel", "models/my model.mdl" }),
                    "an unterminated quote runs to the end of the line rather than " +
                    "discarding the argument, which is what CCommand::Tokenize does");

        Assert.True(CommandTokens.Split("ent_keyvalue door message \"\"").SequenceEqual(
                        new[] { "ent_keyvalue", "door", "message", "" }),
                    "and \"\" is an EMPTY argument, not an absent one — clearing a keyvalue " +
                    "is a thing a person means");
    }

    [Fact]
    public void TabsAndNewlinesSeparateWordsToo()
        => Assert.True(CommandTokens.Split("give\tar2\n").SequenceEqual(new[] { "give", "ar2" }),
                       "a line pasted from a config is still a line");

    //-------------------------------------------------------------------------
    // The sv_cheats gate. Wrong twice in a week: once too early, once absent.
    //-------------------------------------------------------------------------

    [Fact]
    public void EveryCheatCommandIsRefusedWithCheatsOff()
    {
        // BY REFLECTION over the command classes, so a command added tomorrow is
        // covered by this the moment it exists. Four exceptions, each because
        // STOCK'S OWN GATE was not FCVAR_CHEAT and this port does not get to
        // tighten it: give says so for itself below, and kill / explode /
        // ss_kill are ordinary commands in every Source game — a player may
        // always end their own life, and explode is the same command with a
        // different damage type on it. The native spellings in
        // shell_standalone_player.cpp returned BEFORE reaching their cheat
        // check for exactly this reason.
        //
        // THE EXCEPTIONS ARE NAMED, NOT DERIVED FROM RequiresCheats. Reading
        // the property the commands set would make this test agree with
        // whatever they happen to say, which is a check that cannot fail: a
        // command that silently turned its gate off would pass.
        string[] notCheatGatedInStock = { "give", "kill", "explode", "ss_kill" };

        foreach (var type in DevConsole.All.Where(t => typeof(DevCommand).IsAssignableFrom(t)))
        {
            string verb = DevConsole.NameOf(type);

            if (notCheatGatedInStock.Contains(verb))
                continue;

            var (context, engine, _, _) = Rig(cheats: false);
            Build(type, context).Execute("");

            Assert.True(engine.Calls.Count == 0, $"{verb} touched the engine with cheats off");
        }
    }

    [Fact]
    public void GiveIsNotBehindTheCheatGateInSinglePlayer()
    {
        // Stock's own gate, and it is deliberately NOT FCVAR_CHEAT:
        //     pPlayer && (gpGlobals->maxClients == 1 || sv_cheats->GetBool())
        var (context, _, giver, _) = Rig(cheats: false, singlePlayer: true);

        Run<GiveCommand>(context, WeaponTypes.All[0].Name);

        Assert.True(giver.Weapons.SequenceEqual(new[] { WeaponTypes.All[0].Name }),
                    "give works without cheats in single player, exactly as stock's gate allowed");
    }

    [Fact]
    public void GiveIsRefusedOnAMultiplayerServerWithoutCheats()
    {
        var (context, _, giver, _) = Rig(cheats: false, singlePlayer: false);

        Run<GiveCommand>(context, WeaponTypes.All[0].Name);

        Assert.True(giver.Weapons.Count == 0, "nothing is given without cheats on a multiplayer server");
    }

    [Fact]
    public void NobodyAtTheConsoleIsSaidRatherThanActedOn()
    {
        // The server console with no player reaches every one of these. It used
        // to be answered in C++, before the crossing; it is answered here now,
        // and it must still be answered — acting on EntityHandle.None would
        // create entities at the origin for nobody.
        var (context, engine, _, _) = Rig();
        context.Issuer = () => EntityHandle.None;

        Run<EntCreateCommand>(context, "prop_physics");

        Assert.True(engine.Calls.Count == 0,
                    "with nobody at the console nothing is created — and the console is told so");
    }

    [Fact]
    public void TheIssuerIsTheCommandClientAndNotPlayerOne()
    {
        // THE FACT THE WHOLE CONVERSION TURNS ON. A ConCommand dispatch carries
        // no player, so the temptation is to read player 1 — which is right in
        // exactly the single-player case and silently wrong for the second
        // client on a server, whose ent_create would act as somebody else.
        var engine = Fake();
        engine.Returns("Players.GetCommandClient", Target);

        Assert.True(DevContext.WhoTyped(engine) == Target,
                    "the issuer is stock's UTIL_GetCommandClient, resolved through the engine");
        Assert.True(!engine.Called("Players.GetHandle"),
                    "and the slot walk is not even run when somebody claimed the word");
    }

    [Fact]
    public void TheSolePlayerIsTheIssuerWhenNobodyClaimedTheWord()
    {
        // The server console — which is every tool in this project, the
        // development API included — sets no command client. Stock answered NULL
        // and did nothing there, and so did the version of these commands that
        // preceded this one: ./debug-api cmd "ent_create prop_physics" said "no
        // player issued that" and always would have.
        Assert.True(DevContext.SolePlayer(new[] { EntityHandle.None, Player, EntityHandle.None }) == Player,
                    "with exactly one player connected there is no other candidate, so the " +
                    "family is reachable from the only headless driver there is");
    }

    [Fact]
    public void TwoPlayersAndNoClaimIsARefusalRatherThanAGuess()
        => Assert.True(DevContext.SolePlayer(new[] { Player, Target }) == EntityHandle.None,
                       "with two connected and nobody claiming the word there is no 'the player' " +
                       "to mean, and inventing one would act as somebody who did not type it");

    [Fact]
    public void NobodyConnectedIsARefusalToo()
        => Assert.True(DevContext.SolePlayer(new[] { EntityHandle.None, EntityHandle.None }) == EntityHandle.None,
                       "an empty server has nobody to act for");

    [Fact]
    public void TheSlotWalkOnlyHappensWhenNobodyClaimedTheWord()
    {
        var engine = Fake();
        engine.Returns("Players.GetCommandClient", EntityHandle.None);
        engine.Returns("Players.GetHandle", EntityHandle.None);

        Assert.True(DevContext.WhoTyped(engine) == EntityHandle.None,
                    "an empty server, asked through the engine, is still nobody");
        Assert.True(engine.Called("Players.GetHandle"),
                    "and the slots ARE walked when the command client is empty — the pure rule " +
                    "above would pass happily against a WhoTyped that never called it");
    }

    //-------------------------------------------------------------------------
    // Selection: the picker, then the name, then the class — ent_fire's order.
    //-------------------------------------------------------------------------

    private static DevCommand Selector(DevContext context) => new EntRemoveCommand(context);

    [Fact]
    public void AnEmptyTargetIsWhatThePlayerIsLookingAt()
    {
        var (context, engine, _, _) = Rig();
        engine.Returns("WorldQuery.TracePlayerAim", Target)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));

        var chosen = Selector(context).Select(Player, "");

        Assert.True(chosen.SequenceEqual(new[] { Target }), "an empty target is the picked entity");
        Assert.True(engine.Called("WorldQuery.TracePlayerAim"), "and it is found by tracing the player's aim");
    }

    [Fact]
    public void ANameIsTriedBeforeAClass()
    {
        var (context, engine, _, _) = Rig();
        engine.Returns("Entities.FindAllByName", new List<EntityHandle> { Target });
        engine.Returns("Entities.FindAllByClassname", new[] { Other }.AsEnumerable());

        var chosen = Selector(context).Select(Player, "door");

        Assert.True(chosen.SequenceEqual(new[] { Target }),
                    "a name that matches wins outright — CEventQueue::ServiceEvents' own order, " +
                    "so a map naming one func_button 'func_button' does not fire every button");
        Assert.True(!engine.Called("Entities.FindAllByClassname"),
                    "and the class search is not even run");
    }

    [Fact]
    public void AClassIsTriedWhenNoNameMatches()
    {
        var (context, engine, _, _) = Rig();
        engine.Returns("Entities.FindAllByName", new List<EntityHandle>());
        engine.Returns("Entities.FindAllByClassname", new[] { Target, Other }.AsEnumerable());

        var chosen = Selector(context).Select(Player, "prop_physics");

        Assert.True(chosen.SequenceEqual(new[] { Target, Other }),
                    "with nothing of that name, EVERY entity of that class answers");
    }

    [Fact]
    public void AnEmptySelectionIsAnEmptyList()
    {
        var (context, engine, _, _) = Rig();
        engine.Returns("Entities.FindAllByName", new List<EntityHandle>());
        engine.Returns("Entities.FindAllByClassname", Array.Empty<EntityHandle>().AsEnumerable());

        Assert.True(Selector(context).Select(Player, "nosuchthing").Count == 0,
                    "nothing answers, and the caller gets an empty list rather than a NULL " +
                    "whose while loop silently does not run — stock's shape, and the bug");
    }

    [Fact]
    public void NothingUnderTheCrosshairSelectsNothing()
    {
        var (context, engine, _, _) = Rig();
        engine.Returns("WorldQuery.TracePlayerAim", EntityHandle.None)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));

        Assert.True(Selector(context).Select(Player, "").Count == 0,
                    "looking at nothing, with no target named, selects nothing");
    }

    //-------------------------------------------------------------------------
    // Creation.
    //-------------------------------------------------------------------------

    [Fact]
    public void EntCreateBuildsSpawnsAndPlacesTheEntity()
    {
        var (context, engine, _, directory) = Rig();
        directory.Registered.Add("prop_physics");
        engine.Returns("WorldQuery.TracePlayerAim", EntityHandle.None)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.Create", Target);
        engine.Returns("Entities.SetKeyValue", true);
        engine.Returns("Entities.GetEyeAngles", new Vector(0f, 90f, 0f));
        engine.Returns("Entities.GetIndex", 11);
        engine.Returns("Entities.GetBounds", (Vector.Zero, Vector.Zero));

        Run<EntCreateCommand>(context, "prop_physics model \"props/crate.mdl\"");

        Assert.True(engine.Last("Entities.Create")?.ArgAs<string>(0) == "prop_physics",
                    "the classname written is the classname created");
        Assert.True(engine.Last("Entities.SetKeyValue")?.ArgAs<string>(1) == "model" &&
                    engine.Last("Entities.SetKeyValue")?.ArgAs<string>(2) == "props/crate.mdl",
                    "the quoted keyvalue arrives whole");
        Assert.True(engine.Called("Entities.SetOrigin"), "it is placed where the player is looking");
        Assert.True(engine.Called("Entities.Spawn"), "and it is SPAWNED — a created entity that is " +
                                                     "never spawned is the silent half-failure stock left behind");
    }

    [Fact]
    public void ACreatedEntityFacesBackAtThePlayer()
    {
        var (context, engine, _, directory) = Rig();
        directory.Registered.Add("prop_physics");
        engine.Returns("WorldQuery.TracePlayerAim", EntityHandle.None)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.Create", Target);
        engine.Returns("Entities.SetKeyValue", true);
        engine.Returns("Entities.GetEyeAngles", new Vector(10f, 90f, 0f));
        engine.Returns("Entities.GetIndex", 11);
        engine.Returns("Entities.GetBounds", (Vector.Zero, Vector.Zero));

        Run<EntCreateCommand>(context, "prop_physics");

        var angles = engine.Last("Entities.SetAngles")!.Value.ArgAs<Vector>(1);

        Assert.True(angles.Y == 270f, "a created entity faces back at the player: yaw + 180");
        Assert.True(angles.X == 0f && angles.Z == 0f,
                    "and it is upright — the player's PITCH must not tip a spawned prop over");
    }

    //-------------------------------------------------------------------------
    // An aim that HITS NOTHING. Stock's CC_Ent_Create moved the entity only
    // when `tr.fraction != 1.0` (baseentity.cpp:8564) and otherwise left it at
    // its keyvalue origin; CC_Prop_Physics_Create / CC_Prop_Dynamic_Create
    // returned before creating anything (props.cpp:5835). Ours placed it at the
    // trace's far end. A player standing inside solid traces start-solid along
    // the whole range, so the physics lane's crush probe built its plate 8192
    // units away and read that as a pusher frame bug (A41, 2026-10-05).
    //-------------------------------------------------------------------------

    private static void AimingAtNothing(RecordingEngine engine)
    {
        engine.Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f));
        engine.Returns("WorldQuery.TracePlayerAim", new EntityHandle(0))
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(DevContext.PickRange, 0f, 64f));
        engine.Returns("Entities.Create", Target);
        engine.Returns("Entities.SetKeyValue", true);
        engine.Returns("Entities.GetEyeAngles", new Vector(0f, 90f, 0f));
        engine.Returns("Entities.GetIndex", 11);
        engine.Returns("Entities.GetBounds", (Vector.Zero, Vector.Zero));
        engine.Returns("Entities.FindAllByClassname", Array.Empty<EntityHandle>());
    }

    [Fact]
    public void EntCreateAimedAtNothingKeepsItsOwnOrigin()
    {
        var (context, engine, _, directory) = Rig();
        directory.Registered.Add("info_target");
        AimingAtNothing(engine);

        Run<EntCreateCommand>(context, "info_target origin \"1 2 3\"");

        Assert.True(engine.Called("Entities.Spawn"), "the entity is still created and spawned");
        Assert.False(engine.Called("Entities.SetOrigin"), "but not put at the end of a trace that hit nothing");
        Assert.False(engine.Called("Entities.Teleport"), "nor lifted off a surface it never found");
    }

    [Fact]
    public void PropCreateAimedAtNothingCreatesNothing()
    {
        var (context, engine, _, _) = Rig();
        AimingAtNothing(engine);

        Run<PropPhysicsCreateCommand>(context, "props_junk/wood_crate001a");

        Assert.True(engine.Called("WorldQuery.TracePlayerAim"), "the aim was traced");
        Assert.False(engine.Called("Entities.Create"), "and with no hit, nothing is made");
    }

    //-------------------------------------------------------------------------
    // WHERE a created entity is PUT — lane diag-gateseq.
    //
    // `ent_create` used to set the entity's ORIGIN to the point the aim trace
    // hit, which for a model whose origin is not on its base buries the entity
    // in whatever it was aimed at. Stock did not do that: CC_Ent_Create_DropPoint
    // (baseentity.cpp:8355) raised the trace endpoint by twelve and the caller
    // then dropped the entity onto the floor from there.
    //
    // On dm_lockdown that difference threw `models/props_junk/wood_crate001a.mdl`
    // 84 to 136 units sideways out of the floor it was created inside, and in
    // four measured draws of one round it came out the wrong side and rested at
    // z = -5935 — lane 3f-shadow's "one newly spawned prop falls, and then every
    // one after it in that game".
    //-------------------------------------------------------------------------

    [Fact]
    public void ACreatedEntityIsLiftedClearOfTheSurfaceItWasAimedAt()
    {
        var (context, engine, _, directory) = Rig();
        directory.Registered.Add("prop_physics");
        engine.Returns("WorldQuery.TracePlayerAim", EntityHandle.None)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.Create", Target);
        engine.Returns("Entities.SetKeyValue", true);
        engine.Returns("Entities.GetEyeAngles", new Vector(0f, 90f, 0f));
        engine.Returns("Entities.GetIndex", 11);
        engine.Returns("Entities.GetBounds", (new Vector(-21f, -21f, -21f), new Vector(21f, 21f, 21f)));

        Run<EntCreateCommand>(context, "prop_physics model \"props/crate.mdl\"");

        var placed = engine.Last("Entities.Teleport")?.ArgAs<Vector>(1);

        Assert.True(placed is not null,
                    "the entity is moved off the surface after it is spawned");
        Assert.Equal(33f, placed!.Value.Z);
    }

    [Fact]
    public void TheLiftKeepsTheEntityOverTheSamePointOnTheGround()
    {
        var (context, engine, _, directory) = Rig();
        directory.Registered.Add("prop_physics");
        engine.Returns("WorldQuery.TracePlayerAim", EntityHandle.None)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, -32f, 0f));
        engine.Returns("Entities.Create", Target);
        engine.Returns("Entities.SetKeyValue", true);
        engine.Returns("Entities.GetEyeAngles", new Vector(0f, 90f, 0f));
        engine.Returns("Entities.GetIndex", 11);
        engine.Returns("Entities.GetBounds", (new Vector(-21f, -21f, -21f), new Vector(21f, 21f, 21f)));

        Run<EntCreateCommand>(context, "prop_physics");

        var placed = engine.Last("Entities.Teleport")!.Value.ArgAs<Vector>(1);

        Assert.Equal(64f, placed.X);
        Assert.Equal(-32f, placed.Y);
    }

    [Fact]
    public void TheLiftCarriesNoVelocityIntoTheDrop()
    {
        var (context, engine, _, directory) = Rig();
        directory.Registered.Add("prop_physics");
        engine.Returns("WorldQuery.TracePlayerAim", EntityHandle.None)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.Create", Target);
        engine.Returns("Entities.SetKeyValue", true);
        engine.Returns("Entities.GetEyeAngles", new Vector(0f, 90f, 0f));
        engine.Returns("Entities.GetIndex", 11);
        engine.Returns("Entities.GetBounds", (new Vector(-21f, -21f, -21f), new Vector(21f, 21f, 21f)));

        Run<EntCreateCommand>(context, "prop_physics");

        Assert.True(engine.Last("Entities.Teleport")!.Value.ArgAs<bool>(3),
                    "the lift zeroes velocity, so where it lands does not depend on how long Spawn took");
    }

    [Fact]
    public void TheEntityIsSpawnedBeforeItIsLifted()
    {
        var (context, engine, _, directory) = Rig();
        directory.Registered.Add("prop_physics");
        engine.Returns("WorldQuery.TracePlayerAim", EntityHandle.None)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.Create", Target);
        engine.Returns("Entities.SetKeyValue", true);
        engine.Returns("Entities.GetEyeAngles", new Vector(0f, 90f, 0f));
        engine.Returns("Entities.GetIndex", 11);
        engine.Returns("Entities.GetBounds", (new Vector(-21f, -21f, -21f), new Vector(21f, 21f, 21f)));

        Run<EntCreateCommand>(context, "prop_physics");

        // The box is only the box once the model is loaded, and the model is
        // loaded by Spawn. A lift computed before it would read (0,0,0) and
        // move the entity by stock's twelve and nothing more.
        var order = engine.Calls.Select(c => c.Member).ToList();

        Assert.True(order.IndexOf("Entities.Spawn") >= 0 && order.IndexOf("Entities.GetBounds") >= 0,
                    "both calls were made at all");
        Assert.True(order.IndexOf("Entities.Spawn") < order.IndexOf("Entities.GetBounds"),
                    "the bounds are read AFTER Spawn, or they are not the model's bounds");
    }

    [Fact]
    public void AnEntityWhoseOriginIsAlreadyOnItsBaseGetsStocksTwelveUnitsAndNothingElse()
    {
        // Stock's NPCs and items: mins.Z == 0. CC_Ent_Create_DropPoint raised
        // those by exactly twelve and UTIL_DropToFloor let them fall back, and
        // this rule has to leave that case alone or it is not a port of it.
        Assert.Equal(12f,
                     SpawningCommand.DropPointFor(new Vector(64f, 0f, 100f),
                                                  new Vector(-16f, -16f, 0f)).Z - 100f);
    }

    [Fact]
    public void ACentreOriginPropIsRaisedByItsOwnUndersideAsWell()
    {
        Assert.Equal(12f + 21f,
                     SpawningCommand.DropPointFor(new Vector(64f, 0f, 100f),
                                                  new Vector(-21f, -21f, -21f)).Z - 100f);
    }

    [Fact]
    public void ABoxThatFloatsAboveItsOwnOriginIsNeverPUSHEDDownIntoTheSurface()
    {
        // A positive mins.Z is clamped rather than subtracted. Subtracting it
        // would LOWER the entity into the thing it was aimed at, which is the
        // exact failure this rule exists to stop.
        Assert.Equal(12f,
                     SpawningCommand.DropPointFor(new Vector(64f, 0f, 100f),
                                                  new Vector(-8f, -8f, 40f)).Z - 100f);
    }

    [Fact]
    public void NothingCreatedIsSaidRatherThanShrugged()
    {
        var (context, engine, _, directory) = Rig();
        directory.Registered.Add("prop_physics");
        engine.Returns("WorldQuery.TracePlayerAim", EntityHandle.None)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.Create", EntityHandle.None);

        Run<EntCreateCommand>(context, "prop_physics");

        Assert.True(!engine.Called("Entities.Spawn"),
                    "nothing is spawned when nothing was created — and the console says so");
    }

    [Fact]
    public void PropCreateFillsInTheModelsPrefixAndTheExtension()
    {
        Assert.True(DevCommand.ModelPath("props/crate") == "models/props/crate.mdl",
                    "a bare name is under models/ and ends in .mdl, as stock's " +
                    "Q_snprintf + Q_DefaultExtension made it");
        Assert.True(DevCommand.ModelPath("models/props/crate.mdl") == "models/props/crate.mdl",
                    "a full path is left exactly alone");
        Assert.True(DevCommand.ModelPath("models\\props\\crate") == "models/props/crate.mdl",
                    "a Windows path typed from habit still resolves");
    }

    [Fact]
    public void PropPhysicsCreateMakesAPropOfTheWrittenModel()
    {
        var (context, engine, _, directory) = Rig();
        directory.Registered.Add("prop_physics");
        engine.Returns("WorldQuery.TracePlayerAim", EntityHandle.None)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.Create", Target);
        engine.Returns("Entities.SetKeyValue", true);
        engine.Returns("Entities.GetEyeAngles", Vector.Zero);
        engine.Returns("Entities.GetIndex", 11);
        engine.Returns("Entities.GetBounds", (Vector.Zero, Vector.Zero));

        Run<PropPhysicsCreateCommand>(context, "props_junk/watermelon01");

        Assert.True(engine.Last("Entities.Create")?.ArgAs<string>(0) == "prop_physics",
                    "prop_physics_create creates a prop_physics");
        Assert.True(engine.Last("Entities.SetKeyValue")?.ArgAs<string>(2)
                        == "models/props_junk/watermelon01.mdl",
                    "and the model is the written one, completed");
    }

    [Fact]
    public void PropDynamicCreateMakesTheOtherKind()
    {
        var (context, engine, _, directory) = Rig();
        directory.Registered.Add("prop_dynamic");
        engine.Returns("WorldQuery.TracePlayerAim", EntityHandle.None)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.Create", Target);
        engine.Returns("Entities.SetKeyValue", true);
        engine.Returns("Entities.GetEyeAngles", Vector.Zero);
        engine.Returns("Entities.GetIndex", 11);
        engine.Returns("Entities.GetBounds", (Vector.Zero, Vector.Zero));

        Run<PropDynamicCreateCommand>(context, "props_c17/door01a");

        Assert.True(engine.Last("Entities.Create")?.ArgAs<string>(0) == "prop_dynamic",
                    "the two prop spellings differ in exactly one thing, and it is the class");
    }

    //-------------------------------------------------------------------------
    // Mutation.
    //-------------------------------------------------------------------------

    [Fact]
    public void EntRemoveRemovesEverythingSelected()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("Entities.FindAllByName", new List<EntityHandle>());
        engine.Returns("Entities.FindAllByClassname", new[] { Target, Other }.AsEnumerable());

        Run<EntRemoveCommand>(context, "prop_physics");

        Assert.True(engine.CountOf("Entities.Remove") == 2,
                    "every entity of the class is removed, not just the first");
    }

    [Fact]
    public void EntRemoveRefusesTheIssuer()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("WorldQuery.TracePlayerAim", Player)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));

        Run<EntRemoveCommand>(context);

        Assert.True(!engine.Called("Entities.Remove"),
                    "the player who typed it is refused — stock would UTIL_Remove them, " +
                    "which is a crash nobody meant to keep");
    }

    [Fact]
    public void EntSetnameRenamesThroughTheTargetnameKey()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("WorldQuery.TracePlayerAim", Target)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.SetKeyValue", true);

        Run<EntSetNameCommand>(context, "mydoor");

        var call = engine.Last("Entities.SetKeyValue")!.Value;

        Assert.True(call.ArgAs<string>(1) == "targetname" && call.ArgAs<string>(2) == "mydoor",
                    "ent_setname with no target renames what the player is looking at");
    }

    [Fact]
    public void EntKeyvalueAppliesEveryPairToEverySubject()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("Entities.FindAllByName", new List<EntityHandle> { Target, Other });
        engine.Returns("Entities.SetKeyValue", true);

        Run<EntKeyValueCommand>(context, "doors speed 40 rendercolor \"255 0 0\"");

        Assert.True(engine.CountOf("Entities.SetKeyValue") == 4,
                    "two pairs across two entities is four sets");
        Assert.True(engine.AllOf("Entities.SetKeyValue").Any(c => c.ArgAs<string>(2) == "255 0 0"),
                    "and a quoted colour survives the crossing whole");
    }

    /// <summary>
    /// Stock's CBaseEntity::KeyValue moves the entity on "origin" and
    /// "angles" (SetAbsOrigin / SetAbsAngles), which marks the network state
    /// changed; the shell's arm only records the key, so a running entity moved
    /// by ent_keyvalue moved on the server and never on the client (7e's gate:
    /// server 400 units, client 0).
    /// </summary>
    [Fact]
    public void EntKeyvalueOriginAndAnglesMoveTheEntityAsStockDoes()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("Entities.FindAllByName", new List<EntityHandle> { Target });
        engine.Returns("Entities.SetKeyValue", true);

        Run<EntKeyValueCommand>(context, "fire origin \"10 20 30\" angles \"0 90 0\"");

        Assert.Equal(new Vector(10f, 20f, 30f), engine.Last("Entities.SetOrigin")?.ArgAs<Vector>(1));
        Assert.Equal(new Vector(0f, 90f, 0f), engine.Last("Entities.SetAngles")?.ArgAs<Vector>(1));
        Assert.True(engine.CountOf("Entities.SetKeyValue") == 2, "and both keys still reach the entity's KeyValue");
    }

    [Fact]
    public void EntKeyvalueOfAnotherKeyMovesNothing()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("Entities.FindAllByName", new List<EntityHandle> { Target });
        engine.Returns("Entities.SetKeyValue", true);

        Run<EntKeyValueCommand>(context, "fire speed 40");

        Assert.True(engine.CountOf("Entities.SetKeyValue") == 1, "control: the key was applied");
        Assert.False(engine.Called("Entities.SetOrigin") || engine.Called("Entities.SetAngles"));
    }

    [Fact]
    public void EntKeyvalueWithAnOddNumberOfArgumentsIsRefused()
    {
        var (context, engine, _, _) = Rig();

        Run<EntKeyValueCommand>(context, "doors speed");

        Assert.True(!engine.Called("Entities.SetKeyValue"),
                    "a key with no value sets nothing, and the usage line says so");
    }

    [Fact]
    public void EntRotateAddsToTheYaw()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("WorldQuery.TracePlayerAim", Target)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.GetAngles", new Vector(0f, 30f, 0f));

        Run<EntRotateCommand>(context, "90");

        Assert.True(engine.Last("Entities.SetAngles")!.Value.ArgAs<Vector>(1).Y == 120f,
                    "ent_rotate 90 adds ninety degrees of yaw — which is exactly what stock's " +
                    "MatrixBuildRotateZ composition computed");
    }

    [Fact]
    public void EntRotateWithNoNumberUsesStocksDefault()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("WorldQuery.TracePlayerAim", Target)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.GetAngles", Vector.Zero);

        Run<EntRotateCommand>(context);

        Assert.True(engine.Last("Entities.SetAngles")!.Value.ArgAs<Vector>(1).Y == DevContext.DefaultRotate,
                    "stock's seven and a half degrees, kept");
    }

    [Fact]
    public void EntRotateReadsANamedTargetRatherThanADegreeCount()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("Entities.FindAllByName", new List<EntityHandle> { Target });
        engine.Returns("Entities.GetAngles", Vector.Zero);

        Run<EntRotateCommand>(context, "mydoor");

        Assert.True(engine.Called("Entities.FindAllByName"),
                    "a first argument that is not a number is the TARGET, not a rotation");
        Assert.True(engine.Last("Entities.SetAngles")!.Value.ArgAs<Vector>(1).Y == DevContext.DefaultRotate,
                    "and the rotation falls back to the default");
    }

    [Fact]
    public void FiretargetSendsUseWithTheIssuerAsActivator()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("WorldQuery.TracePlayerAim", Target)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.FireInput", true);

        Run<FireTargetCommand>(context);

        var call = engine.Last("Entities.FireInput")!.Value;

        Assert.True(call.ArgAs<string>(1) == "Use", "firetarget sends Use");
        Assert.True(call.ArgAs<EntityHandle>(3) == Player,
                    "and the ACTIVATOR is the player who typed it, so an input reading " +
                    "!activator sees them — the thing ent_fire had to be taught");
    }

    //-------------------------------------------------------------------------
    // Inspection. These print rather than draw, and the divergence is deliberate.
    //-------------------------------------------------------------------------

    [Fact]
    public void EntBboxReadsTheLocalBoxAndEntAbsboxTheWorldOne()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("WorldQuery.TracePlayerAim", Target)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)));
        engine.Returns("Entities.GetBoundsSize", new Vector(32f, 32f, 72f));
        engine.Returns("Entities.GetOrigin", new Vector(100f, 200f, 300f));

        Run<EntBboxCommand>(context);
        Assert.True(!engine.Called("Entities.GetOrigin"),
                    "ent_bbox is the LOCAL box and needs no origin");

        engine.Clear();
        Run<EntAbsboxCommand>(context);
        Assert.True(engine.Called("Entities.GetOrigin"),
                    "ent_absbox is the world box, so it offsets by the origin");
    }

    [Fact]
    public void EntInfoReportsMoreThanEntName()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("WorldQuery.TracePlayerAim", Target)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.GetOrigin", Vector.Zero);
        engine.Returns("Entities.GetAngles", Vector.Zero);
        engine.Returns("Entities.GetVelocity", Vector.Zero);
        engine.Returns("Entities.GetHealth", 100);
        engine.Returns("Entities.GetBounds", (Vector.Zero, Vector.Zero));
        engine.Returns("Entities.GetSpawnFlags", 0);

        Run<EntNameCommand>(context);
        Assert.True(!engine.Called("Entities.GetHealth"), "ent_name is the short form");

        engine.Clear();
        Run<EntInfoCommand>(context);
        Assert.True(engine.Called("Entities.GetHealth") && engine.Called("Entities.GetBounds"),
                    "ent_info is the long one");
    }

    [Fact]
    public void EntInfoPrintsTheStoredTeamSoAJoinTeamIsVerifiable()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("WorldQuery.TracePlayerAim", Target)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.GetOrigin", Vector.Zero);
        engine.Returns("Entities.GetAngles", Vector.Zero);
        engine.Returns("Entities.GetVelocity", Vector.Zero);
        engine.Returns("Entities.GetHealth", 100);
        engine.Returns("Entities.GetBounds", (Vector.Zero, Vector.Zero));
        engine.Returns("Entities.GetSpawnFlags", 0);
        engine.Returns("Players.GetTeam", 2);   // THE STIMULUS: a non-zero team

        var report = ((DescribeCommand)Build(typeof(EntInfoCommand), context)).Report(Target);

        Assert.Contains("team     2", report);
        Assert.True(engine.Called("Players.GetTeam"), "the line reads the store, not a field");
    }

    //-------------------------------------------------------------------------
    // ent_dump's two new halves: what a map may send an entity, and what it
    // puts on the wire. Both come from the SAME places the development API's
    // class listing and payload route read, so the console and the API cannot
    // come to disagree about an entity.
    //-------------------------------------------------------------------------

    /// <summary>An engine stubbed for everything the long form of a dump reads.</summary>
    private static RecordingEngine Dumpable(RecordingEngine engine, string className = "ss_player")
        => Describable(engine, className)
            .Returns("Entities.GetOrigin", Vector.Zero)
            .Returns("Entities.GetAngles", Vector.Zero)
            .Returns("Entities.GetVelocity", Vector.Zero)
            .Returns("Entities.GetHealth", 100)
            .Returns("Entities.GetBounds", (Vector.Zero, Vector.Zero))
            .Returns("Entities.GetSpawnFlags", 0);

    private static NetSchemaType TwoFieldSchema(string className = "ss_player")
        => new(className, "SsPlayerState", NetSchemaShell.Standalone,
               new[]
               {
                   new NetSchemaField("Health", "int", NetFieldKind.Int, 0, 4, 32),
                   new NetSchemaField("Armour", "int", NetFieldKind.Int, 4, 4, 32),
               },
               layoutBytes: 8, propCount: 2, published: true);

    private static byte[] Bytes(int first, int second)
    {
        byte[] blob = new byte[16];
        BitConverter.GetBytes(first).CopyTo(blob, 0);
        BitConverter.GetBytes(second).CopyTo(blob, 4);
        return blob;
    }

    private static string Dump(DevContext context, INetState netState, EntityHandle subject)
        => ((DescribeCommand)Build(typeof(EntDumpCommand), context, netState)).Report(subject);

    [Fact]
    public void EntDumpNamesTheInputsAndOutputsTheClassDeclares()
    {
        var (context, engine, _, directory) = Rig();
        Dumpable(engine);
        directory.Classes.Add(new ScriptedClassInfo(
            "ss_player", "Entity", "SsPlayer",
            new[] { "AddOutput", "Kill", "SetHealth" },
            new[] { "OnDeath" },
            Array.Empty<ScriptedFieldInfo>(), 1));

        string report = Dump(context, new FakeNetState(), Target);

        Assert.True(report.Contains("inputs   AddOutput, Kill, SetHealth"),
                    "ent_dump lists the entity-I/O inputs its class accepts — the same list " +
                    "the development API's class listing serves, which was the only way to " +
                    "see them before");
        Assert.True(report.Contains("outputs  OnDeath"),
                    "and the outputs, which come free with the same lookup");
    }

    [Fact]
    public void AClassThatDeclaresNoIoSaysNoneRatherThanNothing()
    {
        var (context, engine, _, directory) = Rig();
        Dumpable(engine);
        directory.Classes.Add(new ScriptedClassInfo(
            "ss_player", "Entity", "SsPlayer",
            Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<ScriptedFieldInfo>(), 1));

        string report = Dump(context, new FakeNetState(), Target);

        Assert.True(report.Contains("inputs   none declared"),
                    "a class that accepts nothing says so — a blank line reads as a bug in " +
                    "the dump rather than as a fact about the entity");
    }

    [Fact]
    public void AClassNobodyDeclaresIsToldApartFromOneWithNoInputs()
    {
        // THE DISTINCTION THAT EXPLAINS A DEAD ent_fire. "This entity accepts no
        // inputs" and "nothing on this side declares this class at all" are
        // different answers and only the second explains where an input went;
        // collapsing them would rebuild the silence this family exists to end.
        var (context, engine, _, _) = Rig();
        Dumpable(engine, "func_door");

        string report = Dump(context, new FakeNetState(), Target);

        Assert.True(report.Contains("no managed class declares 'func_door'"),
                    "a native entity's I/O is named as unreadable from here, not as absent");
        Assert.True(!report.Contains("none declared"),
                    "and it is not reported as a class that simply declares nothing");
    }

    [Fact]
    public void EntDumpDecodesTheNetStateThroughTheSameLayoutTheDebugApiUses()
    {
        var (context, engine, _, _) = Rig();
        Dumpable(engine);

        var schema = TwoFieldSchema();
        var net = new FakeNetState
        {
            Schema = schema,
            Blob = new NetEntityBlob(true, 11, Target.Value, "ss_player", Bytes(77, 25)),
        };

        string report = Dump(context, net, Target);

        Assert.True(report.Contains("netstate SsPlayerState on"),
                    "the dump names the C# type and the DT_ table its bytes travel on");

        // THE ANTI-DRIFT CHECK, and the reason no value is formatted by hand
        // here. Every value in the report is NetSchema.Decode's own, so the
        // console and the payload route cannot come to differ about what an
        // entity's bytes say — they are one decoder with two formatters.
        foreach (var field in NetSchema.Decode(schema, net.Blob.Blob))
        {
            Assert.True(report.Contains(field.Field.Name) && report.Contains(field.Value),
                        $"{field.Field.Name} reads {field.Value} in the dump, exactly as the " +
                        $"payload route decodes it");
        }

        Assert.True(report.Contains("77") && report.Contains("25"),
                    "and those values are the bytes that were in the blob, not zeroes");
    }

    [Fact]
    public void AShortBlobIsNamedPerFieldRatherThanReadAsZero()
    {
        var (context, engine, _, _) = Rig();
        Dumpable(engine);

        var net = new FakeNetState
        {
            Schema = TwoFieldSchema(),
            // Room for the first field and not the second. Native sizes the
            // blob for the greediest class, so a short one is a real fault.
            Blob = new NetEntityBlob(true, 11, Target.Value, "ss_player", new byte[4]),
        };

        string report = Dump(context, net, Target);

        Assert.True(report.Contains("Armour") && report.Contains("past the end of a short blob"),
                    "a field the blob is too short to hold is NAMED — a fabricated zero is a " +
                    "lie the reader would act on");
    }

    [Fact]
    public void AClassWithNoNetworkedSchemaSaysSoRatherThanPrintingNothing()
    {
        var (context, engine, _, _) = Rig();
        Dumpable(engine, "func_door");

        string report = Dump(context, new FakeNetState(), Target);

        Assert.True(report.Contains("no networked schema for 'func_door'"),
                    "most native classes have none on this side, and that is an answer");
    }

    [Fact]
    public void NoBytesIsToldApartFromNoSchema()
    {
        var (context, engine, _, _) = Rig();
        Dumpable(engine);

        var net = new FakeNetState
        {
            Schema = TwoFieldSchema(),
            Blob = NetEntityBlob.Missing(11),
        };

        string report = Dump(context, net, Target);

        Assert.True(report.Contains("no bytes"),
                    "a class with a schema whose entity answered nothing is a third state, " +
                    "and it is the one that means the index went stale");
    }

    [Fact]
    public void TheShortFormsStillPrintNeitherHalf()
    {
        var (context, engine, _, directory) = Rig();
        Dumpable(engine);
        directory.Classes.Add(new ScriptedClassInfo(
            "ss_player", "Entity", "SsPlayer", new[] { "Kill" }, new[] { "OnDeath" },
            Array.Empty<ScriptedFieldInfo>(), 1));

        var net = new FakeNetState
        {
            Schema = TwoFieldSchema(),
            Blob = new NetEntityBlob(true, 11, Target.Value, "ss_player", Bytes(77, 25)),
        };

        string report = ((DescribeCommand)Build(typeof(EntNameCommand), context, net)).Report(Target);

        Assert.True(!report.Contains("inputs") && !report.Contains("netstate"),
                    "ent_name is stock's short form and stays short — the long report is what " +
                    "ent_info and ent_dump are for");
    }

    [Fact]
    public void EntViewoffsetReadsTheEyePosition()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("WorldQuery.TracePlayerAim", Target)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.GetOrigin", Vector.Zero);
        engine.Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f));

        Run<EntViewOffsetCommand>(context);

        Assert.True(engine.Called("Entities.GetEyePosition"),
                    "ent_viewoffset asks the engine where the eyes are");
    }

    [Fact]
    public void EntPivotReportsTheAxesAsWellAsThePoint()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("WorldQuery.TracePlayerAim", Target)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("Entities.GetOrigin", Vector.Zero);
        engine.Returns("Entities.GetAngles", new Vector(0f, 90f, 0f));

        Run<EntPivotCommand>(context);

        Assert.True(engine.Called("Entities.GetAngles"),
                    "ent_pivot draws an entity's axes in stock; here it prints them, " +
                    "which still needs the orientation");
    }

    //-------------------------------------------------------------------------
    // The player.
    //-------------------------------------------------------------------------

    [Fact]
    public void GiveNamesAWeaponFromThisModsTable()
    {
        var (context, _, giver, _) = Rig();

        Run<GiveCommand>(context, WeaponTypes.All[1].Name);

        Assert.True(giver.Weapons.SequenceEqual(new[] { WeaponTypes.All[1].Name }),
                    "give hands a registered weapon straight to the arsenal");
    }

    [Fact]
    public void GiveNamesAnAmmoPoolAndFillsItToTheCarryLimit()
    {
        var (context, _, giver, _) = Rig();
        var ammo = AmmoTypes.All[0];

        Run<GiveCommand>(context, ammo.Name);

        Assert.True(giver.Ammo.SequenceEqual(new[] { (ammo.Name, ammo.MaxCarry) }),
                    "give <ammo> with no count fills the pool to its carry limit");

        giver.Ammo.Clear();
        Run<GiveCommand>(context, $"{ammo.Name} 7");

        Assert.True(giver.Ammo.SequenceEqual(new[] { (ammo.Name, 7) }),
                    "and a written count is honoured");
    }

    [Fact]
    public void GiveRefusesAnUnknownNameOutLoudRatherThanSilently()
    {
        var (context, _, giver, _) = Rig();

        Run<GiveCommand>(context, "item_healthkit");

        Assert.True(giver.Weapons.Count == 0 && giver.Ammo.Count == 0,
                    "an unknown name gives nothing — and the console prints what it COULD have " +
                    "given, which is the answer a half-remembered classname wants");
    }

    [Fact]
    public void FovSetsTheZoomAgainstTheIssuerAsItsOwnRequester()
    {
        var (context, engine, _, _) = Rig();

        Run<FovCommand>(context, "30");

        var call = engine.Last("Players.SetFov")!.Value;

        Assert.True(call.ArgAs<int>(2) == 30, "the written number is the FOV");
        Assert.True(call.ArgAs<EntityHandle>(0) == Player && call.ArgAs<EntityHandle>(1) == Player,
                    "the player is their OWN requester, so fov 0 releases the console's zoom " +
                    "and not an env_zoom's — SetFov is keyed by requester exactly so two " +
                    "cannot release each other");
    }

    [Fact]
    public void FovRefusesSomethingThatIsNotANumber()
    {
        var (context, engine, _, _) = Rig();

        Run<FovCommand>(context, "wide");

        Assert.True(!engine.Called("Players.SetFov"), "and nothing was set");
    }

    [Fact]
    public void SetmodelCompletesThePathTheSameWayPropCreateDoes()
    {
        var (context, engine, _, _) = Rig();

        Run<SetModelCommand>(context, "player/alyx");

        Assert.True(engine.Last("EntityRender.SetModel")?.ArgAs<string>(1) == "models/player/alyx.mdl",
                    "one path rule for every command that takes a model");
    }

    //-------------------------------------------------------------------------
    // The world.
    //-------------------------------------------------------------------------

    [Fact]
    public void CastRayTracesFromTheEyeAlongTheAim()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f));
        engine.Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f));
        engine.Returns("WorldQuery.TraceLine",
                       new TraceResult(true, 0.5f, new Vector(100f, 0f, 64f), new Vector(-1f, 0f, 0f), Target));

        Run<CastRayCommand>(context);

        var call = engine.Last("WorldQuery.TraceLine")!.Value;

        Assert.True(call.ArgAs<Vector>(0).Z == 64f, "the trace starts at the eye");
        Assert.True(call.ArgAs<Vector>(1).X == DevContext.PickRange,
                    "and runs the picker's range along the facing");
        Assert.True(call.ArgAs<EntityHandle>(3) == Player,
                    "ignoring the player, or every cast would hit them");
    }

    [Fact]
    public void CastHullSweepsABoxRatherThanARay()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("Entities.GetEyePosition", Vector.Zero);
        engine.Returns("Entities.GetEyeAngles", Vector.Zero);
        engine.Returns("WorldQuery.TraceHull", new HullResult(true, false, Target));

        Run<CastHullCommand>(context);

        var call = engine.Last("WorldQuery.TraceHull")!.Value;

        Assert.True(call.ArgAs<Vector>(3).X == DevContext.CastHullExtent,
                    "cast_hull sweeps stock's sixteen-unit box");
        Assert.True(!engine.Called("WorldQuery.TraceLine"), "and it is not a ray");
    }

    [Fact]
    public void AGameMaterialPrintsAsItsLetter()
    {
        // Live on ss_sandbox, a func_button came back as game material 67, which
        // means nothing to a reader. 67 is 'C' — concrete — and SurfaceMaterial
        // deliberately names only the handful this mod acts on, so the letter is
        // the part that is always meaningful.
        Assert.True(DevCommand.MaterialName((SurfaceMaterial)67) == "'C'",
                    "a material this mod has no name for prints as the letter the engine stores");
        Assert.True(DevCommand.MaterialName(SurfaceMaterial.Metal) == "'M' (Metal)",
                    "and one it does name prints as both");
        Assert.True(DevCommand.MaterialName(SurfaceMaterial.Unknown) == "0 (Unknown)",
                    "zero is not a printable character and must not be shown as one");
        Assert.True(DevCommand.MaterialName(SurfaceMaterial.Silent) == "-1 (Silent)",
                    "nor is stock's explicit make-no-sound");
    }

    [Fact]
    public void GiveSaysWhichHalfOfItsGateItCannotRead()
    {
        // The default answer, not the test seam's: this build has no maxclients
        // to read, so the single-player half of stock's gate can never open and
        // the refusal has to say that rather than implying a server setting the
        // reader could change.
        var (context, _, giver, _) = Rig(cheats: false);
        context.SinglePlayer = new DevContext(Fake(), Fake(), Fake(), Fake(), giver,
                                              new FakeDirectory(), new EntityFactories(), Fake(), new RecordingLog()).SinglePlayer;

        Assert.True(!context.SinglePlayer(),
                    "an access question nobody can answer is a NO, the same direction " +
                    "ent_fire_access.h takes with the dedicated server's autokick test");

        Run<GiveCommand>(context, WeaponTypes.All[0].Name);
        Assert.True(giver.Weapons.Count == 0, "so give is refused, out loud");
    }

    [Fact]
    public void SurfacepropReportsWhatTheCrosshairIsOn()
    {
        var (context, engine, _, _) = Rig();
        Describable(engine);
        engine.Returns("WorldQuery.TracePlayerAim", Target)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(64f, 0f, 0f));
        engine.Returns("WorldQuery.EntityModelSurfaceProp", 42);
        engine.Returns("WorldQuery.SurfacePropGameMaterial", SurfaceMaterial.Metal);

        Run<SurfacePropCommand>(context);

        Assert.True(engine.Last("WorldQuery.SurfacePropGameMaterial")?.ArgAs<int>(0) == 42,
                    "the surfaceprop index the hit model carries is the one asked about");
    }

    [Fact]
    public void SoundscapeFlushForgetsTheIssuersCurrentPick()
    {
        var (context, _, _, _) = Rig();
        var forgotten = new List<EntityHandle>();
        context.FlushSoundscape = forgotten.Add;

        Run<SoundscapeFlushCommand>(context);

        Assert.True(forgotten.SequenceEqual(new[] { Player }),
                    "the issuer's soundscape is forgotten, so the next frame picks again");
    }

    [Fact]
    public void SoundscapeFlushSaysSoWhenNothingOwnsSoundscapes()
    {
        var (context, engine, _, _) = Rig();
        context.FlushSoundscape = null;

        Run<SoundscapeFlushCommand>(context);

        Assert.True(engine.Calls.Count == 0,
                    "nothing is flushed, and the console is told there is nothing to flush");
    }

    //-------------------------------------------------------------------------
    // The retirements. THE POINT OF THE WHOLE PASS: no command is silent.
    //-------------------------------------------------------------------------

    [Fact]
    public void TestStringPoolIsRetiredNotSilentlyGone()
    {
        // tier1's stringpool.cpp registered it; when that object left server.so
        // (plan_server_novalve.md P3) the server stopped answering the word.
        Assert.True(DevConsole.Retirements.ContainsKey("test_stringpool"),
                    "test_stringpool answers with a reason on the server");
    }

    [Fact]
    public void EveryRetiredCommandIsHandledAndCarriesAReason()
    {
        foreach (var type in DevConsole.All.Where(t => typeof(RetiredDevCommand).IsAssignableFrom(t)))
        {
            var retired = (RetiredDevCommand)Activator.CreateInstance(type, new RecordingLog())!;

            Assert.True(retired.Reason.Length > 30,
                        $"{retired.Verb}'s retirement gives an actual reason, not a shrug");
            Assert.True(retired.Verb == DevConsole.NameOf(type),
                        $"{type.Name} prints the name the console registered it under");

            // No constructor argument but the log it says its retirement on
            // (statics to zero), which is what makes "touches no engine" true by
            // construction rather than by care.
            Assert.True(type.GetConstructors().All(c => c.GetParameters().All(p => p.ParameterType == typeof(ILog))),
                        $"{type.Name} takes no services — a retirement has nothing to ask");
        }
    }

    [Fact]
    public void ARetirementIsAnsweredWhateverSvCheatsSays()
    {
        // Deliberately not gated at all: "this build cannot do that" is a more
        // useful answer than "turn on sv_cheats first" for something that will
        // not work either way, and there is no player to check for either.
        Assert.True(!typeof(DevCommand).IsAssignableFrom(typeof(EntCallRetired)),
                    "a retired command is not a DevCommand, so neither gate can reach it");
        Assert.True(DevConsole.Retirements.ContainsKey("ent_call"),
                    "and it is still listed, and still registered");
    }

    /// <summary>
    /// Verdict A of docs/divergences.md, copied out: the forty-three server
    /// ConCommands that were registered and silent.
    /// </summary>
    public static readonly string[] VerdictA =
    {
        "ent_call", "ent_name", "ent_text", "ent_script_dump", "ent_bbox", "ent_absbox",
        "ent_rbox", "ent_attachments", "ent_viewoffset", "ent_remove", "ent_setname",
        "ent_dump", "ent_info", "ent_create", "ent_pivot", "ent_cancelpendingentfires",
        "ent_rotate", "ent_keyvalue", "ent_messages", "ent_autoaim",
        "ent_show_response_criteria", "firetarget",
        "give", "fov", "say", "say_team", "kill", "explode", "killvector", "explodevector",
        "cast_ray", "cast_hull", "use", "setmodel", "test_dispatcheffect",
        "soundscape_flush", "surfaceprop", "physics_debug_entity",
        "prop_dynamic_create", "prop_physics_create", "test_entity_blocker",
        "particle_test_start", "particle_test_stop",
    };

    /// <summary>
    /// The one of the forty-three that is answered somewhere other than the
    /// development console: <c>use</c> is ServerModule's, beside <c>ss_use</c>,
    /// because it IS <c>ss_use</c> — one selection rule under two spellings.
    ///
    /// <para>
    /// <c>kill</c> and <c>explode</c> were here too until lane 3c-entityshell.
    /// They were the shell's own damage path in
    /// <c>shell_standalone_player.cpp</c>, reached through a CON_COMMAND that
    /// routed the word into a native verb table; they are
    /// <see cref="KillCommand"/> and <see cref="ExplodeCommand"/> now, so this
    /// console answers them and the exemption would hide their absence rather
    /// than explain it. They still go through the ordinary damage path, which
    /// was the reason for the exemption in the first place — that reason is now
    /// a line in <c>PlayerSelfCommand.Suicide</c> instead of a line here.
    /// </para>
    /// </summary>
    /// <para>
    /// <c>say</c> and <c>say_team</c> were retired until lane player-msgs: they
    /// are <see cref="SayCommand"/> and <see cref="SayTeamCommand"/> now,
    /// Host_Say's ConCommands, answered by the chat rather than this console.
    /// </para>
    private static readonly string[] AnsweredElsewhere = { "use", "say", "say_team" };

    /// <summary>
    /// Development commands that reached this console by a DIFFERENT route from
    /// Verdict A's forty-three, and so are not on that list and must not be added
    /// to it — Verdict A is the catalogue of the commands that resolved a
    /// <c>CBasePlayer</c> subject, got NULL and returned in silence, and its
    /// count of 43 is a fact about that catalogue rather than about this console.
    ///
    /// <para>
    /// <c>bloodspray</c> was <c>CC_BloodSpray</c>, registered by
    /// <c>game/server/effects.cpp</c>; it moved here when that file left the
    /// server build. It was never silent — it did exactly what it was asked, to
    /// the four SDK entities on the map — so it is not a Verdict A command.
    /// </para>
    ///
    /// <para>
    /// <c>groundlist</c> and <c>bug_swap</c> arrived the same way at the Phase 4
    /// exit, when <c>game/server/client.cpp</c> left <c>server_csharp.vpc</c>,
    /// and both are RETIREMENTS rather than answers. Neither is a Verdict A
    /// command: Verdict A catalogues commands that resolved a
    /// <c>CBasePlayer</c> subject and got NULL, and these two never asked for
    /// one — <c>groundlist</c> walked <c>gEntList</c> and printed each entity's
    /// SDK ground-link list, and <c>bug_swap</c>'s native body had been
    /// <c>return;</c> under SOURCESHARP since step 13c. Registering them here
    /// is strictly better than what the eviction removed, because a silent
    /// no-op becomes a printed one (§7).
    /// </para>
    ///
    /// <para>
    /// A LIST RATHER THAN A LOOSENED ASSERTION. Dropping the
    /// nothing-extra check would have retired the only test that can find a
    /// command whose spelling stock never had; naming the arrival keeps the
    /// check and makes each addition a line somebody wrote a reason on.
    /// </para>
    ///
    /// <para>
    /// <c>ent_fire</c> arrived the same way and is the sharpest case of it. It
    /// is not a Verdict A command by that catalogue's rule: Verdict A is the
    /// forty-three whose registrations this port had already moved, and
    /// <c>ent_fire</c>'s stayed in <c>baseentity.cpp</c> BECAUSE THAT FILE WAS
    /// STILL COMPILED. Under <c>SS_CBASEENTITY_ALIAS</c> the file leaves
    /// <c>server_csharp.vpc</c> and the command left with it, with nothing in
    /// its place -- measured 2026-09-15, armed, <c>ent_fire &lt;a&gt; Kill</c>
    /// left the entity standing and the console said nothing at all, because
    /// every caller here pushes the word through <c>engine-&gt;ServerCommand</c>
    /// and that path prints no <c>Unknown command</c>. So it moves here under
    /// the rule the rest of its family already follows, and the stock block is
    /// guarded on SOURCESHARP so exactly one <c>ent_fire</c> exists in either
    /// configuration. Its own access rule came with it: <c>EntFireAccess</c>,
    /// beside the command, the managed half of
    /// <c>game/server/ent_fire_access.h</c>.
    /// </para>
    ///
    /// <para>
    /// <c>drawline</c> and <c>drawcross</c> joined them in Phase 1b, by the same
    /// road <c>groundlist</c> and <c>bug_swap</c> took: all four registered from
    /// <c>game/server/client.cpp</c>, which left <c>server_csharp.vpc</c>. Two of
    /// the four came across as WORKING native commands in
    /// <c>shell_client_orphans.cpp</c>, on the argument that they needed "no
    /// entity, no ABI slot and no decision". The slot half of that was wrong:
    /// <c>UTIL_AddDebugLine</c> puts a line into the ENGINE's debug overlay pool,
    /// and the ABI published no line primitive -- <c>DrawEntityOverlay</c> is text
    /// on an entity. With that file deleted there was nothing for a managed body
    /// to call, so Phase 1b registered them as retirements that said exactly that
    /// and named the one appended slot that would bring them back.
    /// </para>
    ///
    /// <para>
    /// THEY ARE LIVE AGAIN. Lane 1e-abi appended that slot at ABI 246, and both
    /// are ordinary <c>DevCommand</c>s in <c>DevCommandsLive.cs</c> now. They
    /// stay on this list because the list is about where a command CAME FROM,
    /// which has not changed, and because the question it answers — is every
    /// word that arrived with an evicted file answered by something — is the
    /// same question either way.
    /// </para>
    ///
    /// <para>
    /// <c>test_stringpool</c> came from a library, not the game: tier1's
    /// stringpool.cpp registered it, and it left server.so with that object
    /// (plan_server_novalve.md P3). It is a retirement.
    /// </para>
    /// </summary>
    private static readonly string[] ArrivedWithAnEvictedFile =
        { "bloodspray", "groundlist", "bug_swap", "ent_fire", "drawline", "drawcross", "test_stringpool" };

    /// <summary>
    /// Development commands this port WROTE, which stock never had in any
    /// spelling — a separate list from <see cref="ArrivedWithAnEvictedFile"/>
    /// on purpose, because that one is about commands that MOVED and this one
    /// is about commands that did not exist.
    ///
    /// <para>
    /// <c>playentitysound</c> is plan_entity.md step 4b's gate driver. That
    /// step had to demonstrate a sound emitted from a SHELL-LESS entity's
    /// position, and on dm_lockdown 81 of the 473 live entities are shell-less
    /// and not one of them emits a sound: every managed class that calls
    /// <see cref="IEngineSound.EntityEmitSound"/> is a brush, an item or an
    /// effect, all of which have shells. <c>ent_fire</c> cannot reach one
    /// either, being the ENGINE's command walking the engine's entity list. The
    /// case existed in the code and could not be produced in a running game.
    /// </para>
    ///
    /// <para>
    /// Stock's nearest thing is <c>playgamesound</c>, which plays a script from
    /// the LOCAL PLAYER on the client and cannot name a subject at all — so
    /// this is not that command under a new name, and putting it on Verdict A's
    /// list would corrupt a catalogue whose count of 43 is a fact about stock.
    /// </para>
    /// </summary>
    private static readonly string[] WrittenByThisPort = { "playentitysound" };

    /// <summary>
    /// The player's own commands, which arrived from
    /// <c>shell_standalone_player.cpp</c> in lane 3c-entityshell
    /// (plan_shim.md §5 3b).
    ///
    /// <para>
    /// A THIRD LIST, and the distinction it draws is the same one the first two
    /// draw. <see cref="ArrivedWithAnEvictedFile"/> is about commands that moved
    /// when a stock file left the build; <see cref="WrittenByThisPort"/> is
    /// about commands stock never had. These moved when a SHIM file's policy
    /// left C++, which is neither: every one of them is a spelling stock had
    /// and this tree already answered, just not from here.
    /// </para>
    ///
    /// <para>
    /// <b>Why they are not on Verdict A.</b> That catalogue's count of 43 is a
    /// fact about the commands that resolved a <c>CBasePlayer</c> subject, got
    /// NULL and returned in silence — the state of the tree at one moment.
    /// <c>kill</c> and <c>explode</c> ARE on it and so are not repeated here.
    /// The rest were rescued earlier, by being re-registered in C++ in the
    /// player shell, and they were never silent after that; adding them to
    /// Verdict A would corrupt a count that means something.
    /// </para>
    ///
    /// <para>
    /// <b>The four <c>ss_</c> spellings are not stock's and are not new
    /// either.</b> They are the hierarchy-explicit names that worked while the
    /// stock ones were dead, so they are in fingers and in this tree's own
    /// tooling, and each shares a body with its stock twin rather than
    /// restating it — two separately implemented spellings of one command are
    /// two commands waiting to disagree.
    /// </para>
    /// </summary>
    private static readonly string[] ArrivedFromThePlayerShell =
    {
        "hurtme", "noclip", "notarget", "setang", "setang_exact", "setpos", "setpos_exact",
        "ss_hurtme", "ss_kill", "ss_noclip", "ss_setpos",
    };

    [Fact]
    public void EveryCommandInVerdictAIsAccountedFor()
    {
        // THE CHECK THAT WOULD HAVE CAUGHT IT. Three spellings — use,
        // particle_test_start and particle_test_stop — were guarded out of the
        // stock files and never re-registered, so for one build they answered
        // "Unknown command": still silent, by a different route. Nothing in the
        // suite noticed, because every test here asks about a verb it already
        // knows exists. This asks the opposite question — is every name on the
        // ORIGINAL list answered by something — and that is the only direction
        // that can find a name nobody wrote a test for.
        //
        // It now reads the REGISTRATIONS rather than a table beside them, which
        // is what makes it a check on the console instead of on a list.
        var missing = VerdictA
            .Where(v => !DevConsole.Verbs.Contains(v)
                     && !DevConsole.Retirements.ContainsKey(v)
                     && !AnsweredElsewhere.Contains(v))
            .ToList();

        Assert.True(missing.Count == 0,
                    $"every command in Verdict A is answered, retired, or owned elsewhere; " +
                    $"these are not: {string.Join(", ", missing)}");

        Assert.True(VerdictA.Length == 43, "Verdict A is forty-three commands");
        Assert.True(VerdictA.Distinct().Count() == 43, "and none of them is listed twice");
    }

    [Fact]
    public void NothingIsAnsweredThatWasNotOnTheList()
    {
        // The other direction, and it is not pedantry: a development command
        // whose spelling stock never had would answer to a word the console does
        // not know, and one that duplicates a gameplay verb would SHADOW it —
        // which is exactly what the crossing's gate did to lastinv for a while.
        var extra = DevConsole.Verbs.Concat(DevConsole.Retirements.Keys)
                                    .Where(v => !VerdictA.Contains(v)
                                             && !ArrivedWithAnEvictedFile.Contains(v)
                                             && !WrittenByThisPort.Contains(v)
                                             && !ArrivedFromThePlayerShell.Contains(v))
                                    .ToList();

        Assert.True(extra.Count == 0,
                    $"the development console answers only what Verdict A listed, plus the " +
                    $"commands ArrivedWithAnEvictedFile, WrittenByThisPort and " +
                    $"ArrivedFromThePlayerShell name; these are new: {string.Join(", ", extra)}");

        // AND THE EXEMPTION LIST IS ITSELF ANSWERED. A name could be written
        // there and its class never registered, which would make the exemption
        // hide the very absence this test exists to find.
        var unanswered = ArrivedWithAnEvictedFile
            .Concat(WrittenByThisPort)
            .Concat(ArrivedFromThePlayerShell)
            .Where(v => !DevConsole.Verbs.Contains(v) && !DevConsole.Retirements.ContainsKey(v))
            .ToList();

        Assert.True(unanswered.Count == 0,
                    $"every command ArrivedWithAnEvictedFile, WrittenByThisPort and " +
                    $"ArrivedFromThePlayerShell name is answered by a class: " +
                    $"{string.Join(", ", unanswered)}");
    }

    /// <summary>
    /// THEY DRAW AGAIN, and this fact is the one that used to say the
    /// opposite.
    ///
    /// <para>
    /// Phase 1b deleted <c>shell_client_orphans.cpp</c>, where <c>drawline</c>
    /// and <c>drawcross</c> were working native commands, and retired them
    /// because the ABI published no line primitive — <c>DrawEntityOverlay</c>
    /// is TEXT on an ENTITY. Each retirement named the one appended slot that
    /// would lift it, <c>AddLineOverlay</c>, and lane 1e-abi appended it. So
    /// the pair is live, and the assertion flips with them rather than being
    /// deleted: a word that answers nothing at all is the failure both states
    /// of this fact exist to exclude.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTwoDebugDrawCommandsAreLiveRatherThanRetired()
    {
        foreach (var verb in new[] { "drawline", "drawcross" })
        {
            Assert.True(DevConsole.Verbs.Contains(verb),
                verb + " is not a live command. AddLineOverlay is on the ABI since "
                     + "246, so there is a line primitive for it to call.");
            Assert.False(DevConsole.Retirements.ContainsKey(verb),
                verb + " is still registered as a retirement as well, so the console "
                     + "has two classes for one word and keeps whichever it saw first.");
        }
    }

    /// <summary>
    /// And the word still ANSWERS, which is the property that survives the
    /// flip. Deleting a registration makes the console say "Unknown command",
    /// which reads as a typo rather than as anything deliberate, and that is
    /// what the three <c>particle_test</c> spellings cost once already.
    /// </summary>
    [Fact]
    public void TheTwoDebugDrawCommandsAreStillAnsweredByAClass()
    {
        foreach (var verb in new[] { "drawline", "drawcross" })
        {
            Assert.True(DevConsole.All.Count(t => DevConsole.NameOf(t) == verb) == 1,
                verb + " is answered by exactly one registered class");
        }
    }

    [Fact]
    public void NoDevelopmentCommandShadowsAGameplayVerb()
    {
        // What AWordThisClassDoesNotOwnFallsThrough used to ask, asked at the
        // level where it can now go wrong. There is no dispatcher to swallow a
        // word any more — a name is a class — so the hazard is not a fallthrough
        // but a COLLISION: two registrations of one spelling, of which the
        // console keeps one and silently drops the other.
        string[] gameplay = { "ss_use", "ss_slot", "invnext", "invprev", "lastinv", "god", "buddha", "use" };

        var clash = DevConsole.Verbs.Concat(DevConsole.Retirements.Keys)
                                    .Where(gameplay.Contains)
                                    .ToList();

        Assert.True(clash.Count == 0,
                    $"the development console registers none of the gameplay verbs " +
                    $"ServerModule.OnClientCommand answers: {string.Join(", ", clash)}");
    }

    [Fact]
    public void NoNameIsRegisteredTwice()
    {
        var names = DevConsole.All.Select(DevConsole.NameOf).ToList();

        var duplicated = names.GroupBy(n => n, StringComparer.Ordinal)
                              .Where(g => g.Count() > 1)
                              .Select(g => g.Key)
                              .ToList();

        Assert.True(duplicated.Count == 0,
                    $"one spelling is one class — tier1 keeps the FIRST ConCommand of a name and " +
                    $"throws the rest away, so a duplicate is a command that exists and never " +
                    $"runs: {string.Join(", ", duplicated)}");
    }

    [Fact]
    public void EveryDevelopmentCommandIsLowerCaseAndCarriesHelp()
    {
        foreach (var type in DevConsole.All)
        {
            string name = DevConsole.NameOf(type);

            Assert.True(name == name.ToLowerInvariant(),
                        $"'{name}' is written the way the console lists it");

            // The thing a table entry could not have. `help ent_create` printed
            // nothing at all while these were rows in a switch.
            Assert.True(DevConsole.HelpOf(type).Length > 10,
                        $"'{name}' registers help text the console can print");
        }
    }

    [Fact]
    public void EveryDevelopmentCommandIsActuallyRegistered()
    {
        // THE ONE THE PREDECESSOR COULD NOT ASK. Its verb table said what it
        // would answer to; whether the console had a registration for that word
        // was a separate fact in a separate file, and the two disagreed for a
        // build. Here the discovery pass that registers cs_hello is run over the
        // same assembly the game runs it over, and every development command has
        // to come out of it — a class that cannot be BUILT (a constructor asking
        // for something the container has not got) is skipped with a warning in
        // the game, which is exactly the silent absence this catches.
        var services = new ServiceContainer();
        var engine = Fake();
        services.AddInstance(new DevContext(engine, engine, engine, engine,
                                            new RecordingGiver(), new FakeDirectory(), new EntityFactories(), engine, new RecordingLog()));
        services.AddInstance<INetState>(new FakeNetState());

        // bloodspray's dependency, and playentitysound's. The game's container
        // has every engine area in it (EngineServices.AddTo in ServerModule),
        // so these are the same leaves the game supplies rather than a
        // convenience for the test. A command whose dependency is MISSING here
        // is skipped with a warning in the game, which is exactly the silent
        // absence this fact exists to catch — so each one is added by name.
        services.AddInstance<IEngineEffects>(engine);
        services.AddInstance<IEngineSound>(engine);

        // ent_fire's, and it is the whole point of this fact rather than a
        // nuisance: EntFireCommand asks for IEngineHost (ent_fire is the one
        // dev command with an access rule of its own, and the rule asks whether
        // this is a dedicated server). Leaving it out made the command vanish
        // from the registry in SILENCE -- which is exactly how it would vanish
        // from a game whose container was missing it, and exactly the failure
        // ent_fire has just been rescued from. ServerModule's container has it
        // (_services.Require<IEngineHost>()).
services.AddInstance<ILog>(new RecordingLog());
                services.AddInstance<IEngineHost>(engine);

        // drawline's and drawcross's, and it is ent_fire's lesson again rather
        // than a second nuisance: DebugDrawCommand asks for IEngineHud, which
        // is where the appended AddLineOverlay slot lives. Leaving it out makes
        // both commands vanish from the registry in SILENCE -- the same silence
        // they were retired OUT LOUD to avoid. ServerModule's container has it
        // (_services.Require<IEngineHud>()).
        services.AddInstance<IEngineHud>(engine);

        // The player's own commands, and ent_fire's lesson a third time.
        // PlayerSelfCommand asks for IEngineDamage (the suicide path) and
        // IEnginePlayerMovement (the two crossings lane 3c-entityshell added
        // for setang and noclip). Leaving either out makes eleven commands
        // vanish from the registry in SILENCE, which is the same silence this
        // whole family was ported out of C++ to end. ServerModule's container
        // has both (_services.Require<IEngineDamage>() at DamageRules.Register,
        // _services.Require<IEnginePlayerMovement>() at Movement.Register).
        services.AddInstance<IEngineDamage>(engine);
        services.AddInstance<IEnginePlayerMovement>(engine);

        // god and buddha are registrations over a rule that already existed;
        // the toggles are DamageCheats' and are in the game's container
        // (ServerModule adds the instance beside the arsenal decorator).
        services.AddInstance(new DamageCheats(new RecordingLog(), new FakeDirectory(), Fake().Ignores("Console.FindConVarValue")));
        services.AddInstance<IEnginePlayers>(engine);

        var registry = new ConCommandRegistry();
        registry.RegisterAll(typeof(DevConsole).Assembly, TestServices.UnattachedConsole(), services);

        var unregistered = DevConsole.All
            .Select(DevConsole.NameOf)
            .Where(name => registry.ByName(name) is null)
            .ToList();

        Assert.True(unregistered.Count == 0,
                    $"every development command is discovered and bound by the ordinary " +
                    $"[ConCommand] pass: {string.Join(", ", unregistered)}");
    }

    [Fact]
    public void TheVerbIsMatchedWhateverCaseItIsTypedIn()
    {
        var services = new ServiceContainer();
        var engine = Fake();
        services.AddInstance(new DevContext(engine, engine, engine, engine,
                                            new RecordingGiver(), new FakeDirectory(), new EntityFactories(), engine, new RecordingLog()));
        services.AddInstance<INetState>(new FakeNetState());

        // bloodspray's dependency. The game's container has every engine area
        // in it (EngineServices.AddTo in ServerModule), so this is the same
        // leaf the game supplies rather than a convenience for the test.
        services.AddInstance<IEngineEffects>(engine);

        var registry = new ConCommandRegistry();
        registry.RegisterAll(typeof(DevConsole).Assembly, TestServices.UnattachedConsole(), services);

        Assert.True(registry.ByName("GIVE") is GiveCommand,
                    "a console does not care about case, and neither does the registry it feeds");
    }

    //-------------------------------------------------------------------------
    // bloodspray — the one command that came out of game/server/effects.cpp
    // when that file left the server build.
    //
    // Stock's CC_BloodSpray walked gEntList.FindEntityGeneric, which sees the
    // SDK hierarchy only: on ss_sandbox that is 4 entities out of 884. What is
    // pinned here is the arithmetic (stock's spray point), the flags (stock's
    // FX_BLOODSPRAY_ALL, which is three bits as far as anything that reads it
    // is concerned), and that it acts on EVERY entity the name matched rather
    // than the first.
    //-------------------------------------------------------------------------

    [Fact]
    public void BloodSprayPutsTheSprayWhereStockPutIt()
    {
        // Stock:
        //     (forward * 4) + (pEnt->EyePosition() + pEnt->WorldSpaceCenter()) * 0.5f
        var eye = new Vector(0f, 0f, 64f);
        var center = new Vector(0f, 0f, 32f);
        var forward = new Vector(1f, 0f, 0f);

        var where = BloodSprayCommand.SprayOrigin(eye, center, forward);

        Assert.True(where.X == 4f, "the spray starts four units out along the entity's forward");
        Assert.True(where.Y == 0f, "and is not displaced sideways");
        Assert.True(where.Z == 48f, "at the point half way between the eyes and the middle of the body");
    }

    [Fact]
    public void BloodSprayAsksForAllThreeOfTheClientsBloodEffects()
    {
        // FX_BLOODSPRAY_ALL is 0xFF in shareddefs.h, but game/client/fx_blood.cpp
        // reads DROPS, GORE and CLOUD and nothing else — so all three named IS
        // 0xFF to every reader of it. effects_evicted_test.cpp asserts the
        // client still reads exactly those three, which is the half of this
        // that C# cannot see.
        Assert.True(BloodSprayCommand.Everything.HasFlag(BloodSpray.Drops),
                    "stock's FX_BLOODSPRAY_ALL includes the drops");
        Assert.True(BloodSprayCommand.Everything.HasFlag(BloodSpray.Gore),
                    "and the gore");
        Assert.True(BloodSprayCommand.Everything.HasFlag(BloodSpray.Cloud),
                    "and the cloud");
        Assert.True((int)BloodSprayCommand.Everything == 0x07,
                    "and nothing else — a fourth bit here would be an effect the client cannot draw");
    }

    [Fact]
    public void BloodSprayBleedsEveryEntityTheNameMatched()
    {
        var (context, engine, _, _) = Rig();

        Describable(engine, "ss_prop_physics");
        engine.Returns("Entities.FindAllByName", new List<EntityHandle> { Target, Other })
              .Returns("Entities.GetAngles", Vector.Zero)
              .Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f))
              .Returns("Entities.GetWorldSpaceCenter", new Vector(0f, 0f, 32f));

        Run<BloodSprayCommand>(context, "crate");

        Assert.True(engine.CountOf("Effects.BloodEffect") == 2,
                    "both entities of that name bleed — stock's while loop ran over all of them, "
                    + "and a foreach that stopped at the first would look identical on a map with one");

        var call = engine.Calls.First(c => c.Member == "Effects.BloodEffect");

        Assert.True((BloodColor)call.Args[2]! == BloodColor.Red,
                    "stock's console command sprays BLOOD_COLOR_RED whatever the entity is");
        Assert.True((float)call.Args[3]! == 4f,
                    "and stock's amount of 4");
        Assert.True((bool)call.Args[4]! == false,
                    "as drips rather than as a stream, which is what an amount of 4 with no stream flag is");
        // SPELLED OUT rather than compared with BloodSprayCommand.Everything.
        // Comparing the recorded value with the constant the command passed is
        // true whatever the constant holds — one of this tree's thirty checks
        // that cannot fail, caught here by mutating Everything and watching only
        // the OTHER test go red.
        Assert.True((BloodSpray)call.Args[5]!
                        == (BloodSpray.Drops | BloodSpray.Gore | BloodSpray.Cloud),
                    "and asks for the drops, the gore and the cloud, as stock's "
                    + "FX_BLOODSPRAY_ALL did");
    }

    [Fact]
    public void BloodSprayOnANameThatMatchesNothingBleedsNothing()
    {
        var (context, engine, _, _) = Rig();

        engine.Returns("Entities.FindAllByName", new List<EntityHandle>())
              .Returns("Entities.FindAllByClassname", (IEnumerable<EntityHandle>)[]);

        Run<BloodSprayCommand>(context, "nothing_is_called_this");

        Assert.True(engine.CountOf("Effects.BloodEffect") == 0,
                    "nothing bleeds when nothing matched — Select says so on the console instead");
    }

    //-------------------------------------------------------------------------
    // playentitysound — plan_entity.md step 4b's gate driver.
    //-------------------------------------------------------------------------

    [Fact]
    public void PlayEntitySoundEmitsTheNamedScriptFromEverySubject()
    {
        var (context, engine, _, _) = Rig();

        Describable(engine, "ss_func_door");
        engine.Returns("Entities.FindAllByName", new List<EntityHandle> { Target, Other })
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Returns("Entities.GetWorldSpaceCenter", new Vector(128f, 64f, 32f));

        Run<PlayEntitySoundCommand>(context, "Doors.Move18 mydoor");

        Assert.True(engine.CountOf("Sound.EntityEmitSound") == 2,
                    "both entities of that name emit — a foreach that stopped at the first "
                  + "would look identical on a map with one");
    }

    [Fact]
    public void PlayEntitySoundEmitsTheScriptItWasGiven()
    {
        var (context, engine, _, _) = Rig();

        Describable(engine, "ss_func_door");
        engine.Returns("Entities.FindAllByName", new List<EntityHandle> { Target })
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Returns("Entities.GetWorldSpaceCenter", Vector.Zero);

        Run<PlayEntitySoundCommand>(context, "Doors.Move18 mydoor");

        // The SCRIPT is the first argument and the SUBJECT is the rest. Getting
        // that order wrong emits the entity's name as a soundscript, which the
        // engine answers with silence and no message at all -- the failure this
        // whole file exists to stop being invisible.
        Assert.True(engine.Calls.Any(c => c.Member == "Sound.EntityEmitSound"
                                       && c.ArgAs<string>(1) == "Doors.Move18"),
                    "the soundscript emitted is the first argument, not the target's name");
    }

    [Fact]
    public void PlayEntitySoundWithNoArgumentsSaysSoRatherThanEmittingNothing()
    {
        var (context, engine, _, _) = Rig();

        Run<PlayEntitySoundCommand>(context, "");

        Assert.True(engine.CountOf("Sound.EntityEmitSound") == 0,
                    "a line with no soundscript emits nothing");
        Assert.True(engine.CountOf("Entities.FindAllByName") == 0,
                    "and does not even look for a subject -- the usage line is the answer, "
                  + "and searching first would report `nothing is named ''` instead");
    }

    [Fact]
    public void PlayEntitySoundNeedsCheats()
    {
        var (context, engine, _, _) = Rig(cheats: false);

        Run<PlayEntitySoundCommand>(context, "Doors.Move18 mydoor");

        Assert.True(engine.CountOf("Sound.EntityEmitSound") == 0,
                    "it is a cheat command, like every other subject-naming command here");
    }
}
