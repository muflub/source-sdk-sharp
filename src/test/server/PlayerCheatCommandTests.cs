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
/// The player's own console commands, ported out of
/// <c>shell_standalone_player.cpp</c> (plan_shim.md §5 3b, lane 3c-entityshell).
///
/// <para>
/// <b>WHAT THE PORT ACTUALLY MOVED.</b> Not fifteen registrations — a decision
/// each. Which damage type a suicide carries, whether a cheat is refused out
/// loud, whether <c>setpos</c> clears velocity, what <c>notarget</c> should say
/// in a build with no AI. Those are game rules and were in C++ for one reason:
/// two of the six things they do had no managed crossing. <c>setang</c> needs
/// <c>CPlayerState::fixangle</c> and <c>noclip</c> needs a move-type write from
/// between frames. Both crossings exist now, so the rules came over.
/// </para>
///
/// <para>
/// <b>WHY EACH FACT IS ABOUT AN ENGINE CALL OR A PRINTED LINE.</b> There is no
/// third thing a console command does. Every failure this family has ever had
/// was a command that ran and touched nothing while saying nothing, which is
/// indistinguishable from a command that is not registered at all — so
/// asserting "nothing happened" alone would be a check that cannot fail for
/// exactly the defect being guarded. Every refusal below names the line.
/// </para>
/// </summary>
public class PlayerCheatCommandTests
{
    private static readonly EntityHandle Player = new(7u);

    private sealed class NoGiver : IWeaponGiver
    {
        public bool GiveWeapon(EntityHandle player, string className) => false;
        public int GiveAmmo(EntityHandle player, int count, string ammoName) => 0;
    }

    private sealed class EmptyDirectory : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle handle) => null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => false;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>
    /// One command, built and run with the console CAPTURED, over a recorder
    /// that is every engine area at once.
    /// </summary>
    private static (RecordingEngine Engine, string Console) Run<T>(
        string args = "", bool cheats = true, bool haveIssuer = true,
        MoveType previousMoveType = MoveType.Walk)
        where T : DevCommand
    {
        var engine = Fake();

        engine.Returns("PlayerMovement.SetMoveType", previousMoveType)
              .Ignores("Entities.TakeDamage")
              .Ignores("Damage.Apply")
              .Ignores("Entities.SetOrigin")
              .Ignores("Entities.SetVelocity")
              .Ignores("PlayerMovement.SnapEyeAngles");

        var context = new DevContext(engine, engine, engine, engine,
                                     new NoGiver(), new EmptyDirectory(), new EntityFactories(), engine, new RecordingLog())
        {
            CheatsEnabled = () => cheats,
            Issuer = () => haveIssuer ? Player : EntityHandle.None,
        };

        var command = (DevCommand)Activator.CreateInstance(
            typeof(T), context, (IEngineDamage)engine, (IEnginePlayerMovement)engine)!;

        var log = (RecordingLog)context.Log;
        command.Execute(args);

        return (engine, log.Text);
    }

    private static IEnumerable<EngineCall> Of(RecordingEngine engine, string member)
        => engine.Calls.Where(c => c.Member == member);

    private static string NameOf<T>()
        => typeof(T).GetCustomAttributes(typeof(ConCommandAttribute), false)
                    .Cast<ConCommandAttribute>()
                    .Single().Name;

    //-------------------------------------------------------------------------
    // The spellings. Not this port's to choose: a person types god, not ss_god,
    // and tools/ drives the game with setpos and setang.
    //-------------------------------------------------------------------------

    [Theory]
    [InlineData(typeof(KillCommand), "kill")]
    [InlineData(typeof(ExplodeCommand), "explode")]
    [InlineData(typeof(SsKillCommand), "ss_kill")]
    [InlineData(typeof(HurtMeCommand), "hurtme")]
    [InlineData(typeof(SsHurtMeCommand), "ss_hurtme")]
    [InlineData(typeof(SetPosCommand), "setpos")]
    [InlineData(typeof(SetPosExactCommand), "setpos_exact")]
    [InlineData(typeof(SsSetPosCommand), "ss_setpos")]
    [InlineData(typeof(SetAngCommand), "setang")]
    [InlineData(typeof(SetAngExactCommand), "setang_exact")]
    [InlineData(typeof(NoClipCommand), "noclip")]
    [InlineData(typeof(SsNoClipCommand), "ss_noclip")]
    [InlineData(typeof(NoTargetCommand), "notarget")]
    [InlineData(typeof(GodCommand), "god")]
    [InlineData(typeof(BuddhaCommand), "buddha")]
    public void EachCommandRegistersUnderTheSpellingItReplaced(Type type, string expected)
    {
        var attribute = type.GetCustomAttributes(typeof(ConCommandAttribute), false)
                            .Cast<ConCommandAttribute>()
                            .Single();

        Assert.Equal(expected, attribute.Name);
    }

    //-------------------------------------------------------------------------
    // Dying on purpose.
    //-------------------------------------------------------------------------

    [Fact]
    public void KillAppliesEnoughGenericDamageToEndIt()
    {
        var (engine, _) = Run<KillCommand>();

        var hit = Assert.Single(Of(engine, "Damage.Apply"));

        Assert.Equal(Player, hit.ArgAs<EntityHandle>(0));
        Assert.Equal(PlayerSelfCommand.SuicideDamage, hit.ArgAs<float>(2));
        Assert.Equal(DamageType.Generic, hit.ArgAs<DamageType>(3));
    }

    /// <summary>
    /// THE ATTACKER IS NOBODY, which is what makes a suicide read as one rather
    /// than as a kill by the victim — the score, the death message and the
    /// managed death rules all read the attacker. The native spelling passed a
    /// NULL <c>CTakeDamageInfo</c> attacker for the same reason.
    /// </summary>
    [Fact]
    public void ASuicideHasNoAttacker()
    {
        var (engine, _) = Run<KillCommand>();

        Assert.Equal(EntityHandle.None, Assert.Single(Of(engine, "Damage.Apply")).ArgAs<EntityHandle>(1));
    }

    /// <summary>
    /// THE DIFFERENCE BETWEEN kill AND explode IS THE DAMAGE TYPE, and it is
    /// not cosmetic: the type is what the death rule reads to decide gibbing
    /// and the death effect, so a suicide that claimed to be generic would be a
    /// different death from the one that was asked for. <c>kill_helper</c>'s
    /// <c>bExplode</c> picks exactly this in stock.
    /// </summary>
    [Fact]
    public void ExplodeIsTheSameSuicideWithBlastAndAlwaysGibOnIt()
    {
        var (engine, _) = Run<ExplodeCommand>();

        var hit = Assert.Single(Of(engine, "Damage.Apply"));

        Assert.Equal(DamageType.Blast | DamageType.AlwaysGib, hit.ArgAs<DamageType>(3));
        Assert.Equal(PlayerSelfCommand.SuicideDamage, hit.ArgAs<float>(2));
    }

    [Fact]
    public void AlwaysGibIsTheBitSharedDefsSays()
    {
        // DMG_ALWAYSGIB, shareddefs.h:453 -- (1 << 13). A wrong bit here is a
        // death that gibs when it should not, or the reverse, with nothing to
        // say so.
        Assert.Equal(1 << 13, (int)DamageType.AlwaysGib);
    }

    /// <summary>
    /// NEITHER IS CHEAT-GATED, because stock's were not: <c>kill</c> is an
    /// ordinary command in every Source game and <c>explode</c> is the same
    /// command with a different damage type. The native spellings returned
    /// BEFORE reaching their cheat check, which is the same rule written
    /// differently.
    /// </summary>
    [Theory]
    [InlineData(typeof(KillCommand))]
    [InlineData(typeof(ExplodeCommand))]
    [InlineData(typeof(SsKillCommand))]
    public void DyingIsAllowedWithoutCheats(Type type)
    {
        var engine = Fake();
        engine.Ignores("Damage.Apply");

        var context = new DevContext(engine, engine, engine, engine,
                                     new NoGiver(), new EmptyDirectory(), new EntityFactories(), engine, new RecordingLog())
        {
            CheatsEnabled = () => false,
            Issuer = () => Player,
        };

        var command = (DevCommand)Activator.CreateInstance(
            type, context, (IEngineDamage)engine, (IEnginePlayerMovement)engine)!;

        command.Execute("");

        Assert.Single(Of(engine, "Damage.Apply"));
    }

    /// <summary>
    /// SAID. The native kill branch was silent, which is survivable only
    /// because a dead player is visible — and "the command did nothing" and
    /// "the command worked" were therefore the same observation from a console.
    /// </summary>
    [Fact]
    public void AKillSaysThatItKilled()
    {
        var (_, console) = Run<KillCommand>();

        Assert.Contains("killed", console);
    }

    //-------------------------------------------------------------------------
    // Hurting without killing.
    //-------------------------------------------------------------------------

    [Fact]
    public void HurtMeTakesTheAmountItWasGiven()
    {
        var (engine, _) = Run<HurtMeCommand>("25");

        Assert.Equal(25f, Assert.Single(Of(engine, "Damage.Apply")).ArgAs<float>(2));
    }

    [Fact]
    public void HurtMeWithNoArgumentTakesStocksDefault()
    {
        var (engine, _) = Run<HurtMeCommand>();

        Assert.Equal(HurtMeCommand.DefaultDamage, Assert.Single(Of(engine, "Damage.Apply")).ArgAs<float>(2));
    }

    /// <summary>
    /// <see cref="DamageType.PreventPhysicsForce"/>, as the native spelling
    /// passed: the point is to lose health, and a shove from a damage force
    /// moves the player away from whatever they were standing in front of to
    /// measure.
    /// </summary>
    [Fact]
    public void HurtMeDoesNotShoveThePlayer()
    {
        var (engine, _) = Run<HurtMeCommand>("25");

        Assert.Equal(DamageType.PreventPhysicsForce,
                     Assert.Single(Of(engine, "Damage.Apply")).ArgAs<DamageType>(3));
    }

    /// <summary>
    /// A BAD NUMBER IS REFUSED, not read as zero. <c>atof</c> answered 0 for
    /// anything unparseable, so <c>hurtme x</c> applied no damage and said
    /// nothing — indistinguishable from the command being dead, which for a
    /// long time it was.
    /// </summary>
    [Fact]
    public void HurtMeRefusesAnAmountThatIsNotANumber()
    {
        var (engine, console) = Run<HurtMeCommand>("lots");

        Assert.Empty(Of(engine, "Damage.Apply"));
        Assert.Contains("'lots' is not a number", console);
    }

    //-------------------------------------------------------------------------
    // Being somewhere else.
    //-------------------------------------------------------------------------

    [Fact]
    public void SetPosPutsThePlayerWhereItWasTold()
    {
        var (engine, _) = Run<SetPosCommand>("100 -200 300");

        Assert.Equal(new Vector(100f, -200f, 300f),
                     Assert.Single(Of(engine, "Entities.SetOrigin")).ArgAs<Vector>(1));
    }

    /// <summary>
    /// THE VELOCITY IS CLEARED AND THAT IS NOT TIDINESS. A player moved while
    /// falling arrives with the fall still on them and keeps going, which makes
    /// the command read as "setpos put me somewhere near where I asked". The
    /// native spelling cleared it for the same reason.
    /// </summary>
    [Fact]
    public void SetPosClearsTheVelocityItArrivedWith()
    {
        var (engine, _) = Run<SetPosCommand>("100 -200 300");

        Assert.Equal(Vector.Zero, Assert.Single(Of(engine, "Entities.SetVelocity")).ArgAs<Vector>(1));
    }

    [Fact]
    public void SetPosWithTooFewCoordinatesPrintsTheUsageAndMovesNothing()
    {
        var (engine, console) = Run<SetPosCommand>("100 200");

        Assert.Empty(Of(engine, "Entities.SetOrigin"));
        Assert.Contains("usage: setpos", console);
    }

    [Fact]
    public void SetPosRefusesACoordinateThatIsNotANumber()
    {
        var (engine, console) = Run<SetPosCommand>("100 x 300");

        Assert.Empty(Of(engine, "Entities.SetOrigin"));
        Assert.Contains("'x' is not a number", console);
    }

    /// <summary>
    /// <c>setpos_exact</c> and <c>ss_setpos</c> are the SAME rule, shared
    /// rather than restated. The hierarchy's setpos sets the origin outright,
    /// with none of stock's view-height adjustment, so there is no inexact
    /// spelling for the exact one to differ from — and two separately written
    /// bodies would be two commands waiting to disagree.
    /// </summary>
    [Theory]
    [InlineData(typeof(SetPosExactCommand))]
    [InlineData(typeof(SsSetPosCommand))]
    public void TheOtherSetPosSpellingsAreTheSameRule(Type type)
    {
        Assert.True(typeof(SetPosCommand).IsAssignableFrom(type),
                    $"{type.Name} restates setpos instead of sharing it");
    }

    /// <summary>
    /// Stock's <c>setpos_exact</c> is <c>pPlayer->Teleport( &amp;newpos, NULL, NULL )</c>
    /// (client.cpp:1355), and a teleport moves the player's physics shadow with
    /// them (TeleportEntity's <c>pPhys->SetPosition</c>); stock's plain
    /// <c>setpos</c> only sets the origin (client.cpp:1280) and leaves the
    /// shadow to catch up. The velocity rule stays the family's.
    /// </summary>
    [Fact]
    public void SetPosExactTeleportsThePlayerAndTheirShadow()
    {
        var (engine, _) = Run<SetPosExactCommand>("100 -200 300");

        var teleport = Assert.Single(Of(engine, "Entities.Teleport"));
        Assert.Equal(new Vector(100f, -200f, 300f), teleport.ArgAs<Vector>(1));
        Assert.Null(teleport.Args[2]);
        Assert.Equal(true, teleport.Args[3]);
        Assert.Empty(Of(engine, "Entities.SetOrigin"));
    }

    [Fact]
    public void SetPosOnlySetsTheOrigin()
    {
        var (engine, _) = Run<SetPosCommand>("100 -200 300");

        Assert.Single(Of(engine, "Entities.SetOrigin"));
        Assert.Empty(Of(engine, "Entities.Teleport"));
    }

    //-------------------------------------------------------------------------
    // Looking somewhere else.
    //-------------------------------------------------------------------------

    /// <summary>
    /// THROUGH SnapEyeAngles AND NOT THROUGH AN ENTITY-ANGLE WRITE. A player's
    /// VIEW is <c>CPlayerState::v_angle</c>; an entity's angles are where its
    /// model points, and writing one does not move the other. The crossing sets
    /// <c>fixangle</c> with it, which is what makes the client adopt the value
    /// instead of predicting from its own — without that the next usercmd's
    /// stale viewangles undo the snap before anyone sees it.
    /// </summary>
    [Fact]
    public void SetAngSnapsTheViewAndDoesNotWriteEntityAngles()
    {
        var (engine, _) = Run<SetAngCommand>("10 20 30");

        Assert.Equal(new Vector(10f, 20f, 30f),
                     Assert.Single(Of(engine, "PlayerMovement.SnapEyeAngles")).ArgAs<Vector>(1));

        Assert.Empty(Of(engine, "Entities.SetAngles"));
    }

    /// <summary>Roll is optional in stock, and defaults to 0.</summary>
    [Fact]
    public void SetAngWithoutARollSnapsWithNone()
    {
        var (engine, _) = Run<SetAngCommand>("10 20");

        Assert.Equal(new Vector(10f, 20f, 0f),
                     Assert.Single(Of(engine, "PlayerMovement.SnapEyeAngles")).ArgAs<Vector>(1));
    }

    [Fact]
    public void SetAngWithoutAYawPrintsTheUsageAndSnapsNothing()
    {
        var (engine, console) = Run<SetAngCommand>("10");

        Assert.Empty(Of(engine, "PlayerMovement.SnapEyeAngles"));
        Assert.Contains("usage: setang", console);
    }

    /// <summary>
    /// A PRESENT BAD WORD IS STILL A REFUSAL even where the argument is
    /// optional. "Absent" and "unparseable" are different things, and the
    /// native atof read both as 0.
    /// </summary>
    [Fact]
    public void SetAngRefusesARollThatIsNotANumber()
    {
        var (engine, console) = Run<SetAngCommand>("10 20 sideways");

        Assert.Empty(Of(engine, "PlayerMovement.SnapEyeAngles"));
        Assert.Contains("'sideways' is not a number", console);
    }

    [Fact]
    public void SetAngExactIsTheSameRule()
    {
        Assert.True(typeof(SetAngCommand).IsAssignableFrom(typeof(SetAngExactCommand)),
                    "setang_exact restates setang instead of sharing it");
    }

    //-------------------------------------------------------------------------
    // Not being solid.
    //-------------------------------------------------------------------------

    /// <summary>
    /// ONE CROSSING TO TURN IT ON. <c>SetMoveType</c> answers the move type the
    /// player HAD, so the toggle needs no separate read — and a read and a
    /// write with a frame between them would be a frame of movement decided by
    /// the state being toggled.
    /// </summary>
    [Fact]
    public void NoClipOnIsASingleCrossing()
    {
        var (engine, console) = Run<NoClipCommand>(previousMoveType: MoveType.Walk);

        var write = Assert.Single(Of(engine, "PlayerMovement.SetMoveType"));

        Assert.Equal(MoveType.NoClip, write.ArgAs<MoveType>(1));
        Assert.Contains("noclip ON", console);
    }

    [Fact]
    public void NoClipOffPutsWalkingBack()
    {
        var (engine, console) = Run<NoClipCommand>(previousMoveType: MoveType.NoClip);

        var writes = Of(engine, "PlayerMovement.SetMoveType").ToList();

        Assert.Equal(2, writes.Count);
        Assert.Equal(MoveType.Walk, writes[1].ArgAs<MoveType>(1));
        Assert.Contains("noclip OFF", console);
    }

    /// <summary>
    /// A PLAYER IN ANY OTHER MOVE TYPE WALKS OUT OF THIS, which is what stock's
    /// toggle did: it compared against MOVETYPE_NOCLIP and put back
    /// MOVETYPE_WALK rather than remembering what the player had been in.
    /// </summary>
    [Fact]
    public void NoClipFromLadderOrStepTurnsItOn()
    {
        var (engine, console) = Run<NoClipCommand>(previousMoveType: MoveType.Step);

        Assert.Single(Of(engine, "PlayerMovement.SetMoveType"));
        Assert.Contains("noclip ON", console);
    }

    /// <summary>
    /// NOT A PLAYER IS TOLD APART FROM THE TOGGLE. No player is ever in
    /// <see cref="MoveType.None"/>, so that answer is the crossing saying it
    /// could not resolve the handle rather than reporting a state — and
    /// reporting "noclip ON" over a handle nothing happened to is the exact
    /// shape of silence this port exists to remove.
    /// </summary>
    [Fact]
    public void NoClipOnSomethingThatIsNotAPlayerSaysSo()
    {
        var (_, console) = Run<NoClipCommand>(previousMoveType: MoveType.None);

        Assert.Contains("not a hierarchy player", console);
        Assert.DoesNotContain("noclip ON", console);
    }

    //-------------------------------------------------------------------------
    // Hiding from nobody.
    //-------------------------------------------------------------------------

    /// <summary>
    /// STOCK'S RULE HAS NOTHING TO ACT ON, and saying so is the whole reason
    /// the command is registered. <c>notarget</c> sets FL_NOTARGET, read by
    /// <c>CAI_BaseNPC</c>'s target selection, and this build compiles no AI. A
    /// flag nothing reads is not a cheat; it is a lie with a confirmation
    /// message.
    /// </summary>
    [Fact]
    public void NoTargetSaysThereIsNobodyToHideFrom()
    {
        var (engine, console) = Run<NoTargetCommand>();

        Assert.Empty(engine.Calls);
        Assert.Contains("nobody to hide from", console);
    }

    //-------------------------------------------------------------------------
    // The gate, and the missing issuer.
    //-------------------------------------------------------------------------

    /// <summary>
    /// REFUSED OUT LOUD, in stock's own words. A gate that swallows a command
    /// silently is how god and buddha stayed dead for so long: the console
    /// accepts the word and nothing at all happens.
    /// </summary>
    [Theory]
    [InlineData(typeof(HurtMeCommand))]
    [InlineData(typeof(SetPosCommand))]
    [InlineData(typeof(SetAngCommand))]
    [InlineData(typeof(NoClipCommand))]
    [InlineData(typeof(NoTargetCommand))]
    public void ACheatIsRefusedOutLoudWithCheatsOff(Type type)
    {
        var engine = Fake();
        var lines = new List<string>();
        var testLog = new RecordingLog { Line = (text, _) => lines.Add(text) };

        var context = new DevContext(engine, engine, engine, engine,
                                     new NoGiver(), new EmptyDirectory(), new EntityFactories(), engine, testLog)
        {
            CheatsEnabled = () => false,
            Issuer = () => Player,
        };

        var command = (DevCommand)Activator.CreateInstance(
            type, context, (IEngineDamage)engine, (IEnginePlayerMovement)engine)!;

        try
        {
            command.Execute("0 0 0");
        }
        finally
        {
        }

        Assert.Empty(engine.Calls);
        Assert.Contains("sv_cheats", string.Concat(lines));
    }

    /// <summary>
    /// "No player issued that" and "the player ignored it" are the same nothing
    /// from a console, and telling them apart by hand is what this whole family
    /// of bugs has cost. The server console with nobody connected reaches here.
    /// </summary>
    [Fact]
    public void NobodyAtTheConsoleIsSaidRatherThanActedOn()
    {
        var (engine, console) = Run<KillCommand>(haveIssuer: false);

        Assert.Empty(engine.Calls);
        Assert.Contains("no player issued", console);
    }

    //-------------------------------------------------------------------------
    // god and buddha: a registration over a rule that already existed.
    //-------------------------------------------------------------------------

    /// <summary>
    /// NOT <see cref="DevCommand"/>s, and that is deliberate: the sv_cheats
    /// gate for these two is inside <see cref="DamageCheats"/>, where stock's
    /// FCVAR_CHEAT was — on the TOGGLE. Gating in the command as well would
    /// refuse twice and print twice.
    /// </summary>
    [Theory]
    [InlineData(typeof(GodCommand))]
    [InlineData(typeof(BuddhaCommand))]
    public void TheDamageCheatsGateThemselves(Type type)
    {
        Assert.False(typeof(DevCommand).IsAssignableFrom(type),
                     $"{type.Name} would gate the toggle twice; DamageCheats gates itself");
    }

    [Fact]
    public void GodWithNobodyAtTheConsoleIsSaidRatherThanActedOn()
    {
        var engine = Fake();
        engine.Returns("Players.GetCommandClient", EntityHandle.None);

        var lines = new List<string>();
        var testLog = new RecordingLog { Line = (text, _) => lines.Add(text) };

        try
        {
            new GodCommand(testLog, new DamageCheats(testLog, new EmptyDirectory(), Fake().Ignores("Console.FindConVarValue")), engine).Execute("");
        }
        finally
        {
        }

        Assert.Contains("no player issued", string.Concat(lines));
    }
}
