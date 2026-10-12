using System;
using System.Collections.Generic;
using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <c>ent_fire</c> — the command the flip deleted, and the shape of the hole it
/// left.
///
/// <para>
/// <b>WHAT THIS IS ABOUT.</b> Stock registered <c>ent_fire</c> in
/// <c>baseentity.cpp</c>. Under <c>SS_CBASEENTITY_ALIAS</c> that whole TU leaves
/// the vpc, and the command left with it: measured on 2026-09-15, one game each,
/// the same two cells — armed, <c>ent_fire &lt;a&gt; Kill</c> left the entity
/// standing and <c>ent_fire &lt;a&gt; AddOutput</c> left the connection count
/// exactly where it was; unarmed, both worked. Nothing said so. The word was not
/// even reported as an unknown command, because every caller in this tree pushes
/// it through <c>engine-&gt;ServerCommand</c> rather than typing it, and that
/// path prints nothing.
/// </para>
///
/// <para>
/// <b>WHY A TEST AND NOT A LIVE RUN.</b> The live gate DOES catch it — its
/// hot-reload step's wiring assertions are exactly this failure — but it caught
/// it as <i>"187 connection(s), was 187"</i>, five steps and one managed reload
/// away from the cause, and two lanes read that as somebody else's pre-existing
/// drift. These facts name the cause.
/// </para>
///
/// <para>
/// Every one asserts on what the command POSTS, because posting is the whole of
/// what it does: stock's <c>CC_Ent_Fire</c> resolved nothing either — it called
/// <c>g_EventQueue.AddEvent</c> and the queue did the rest. A test that asserted
/// on a delivery would be testing <see cref="EntityDirectory"/>.
/// </para>
/// </summary>
public class EntFireCommandTests
{
    private static readonly EntityHandle Player = new(3u);

    /// <summary>One posted input, as <see cref="DevContext.PostInput"/> sees it.</summary>
    private sealed record Posted(string Target, string Input, string? Value,
                                 float Delay, EntityHandle Activator);

    /// <summary>An entity directory that holds nothing. The queue is the subject, not it.</summary>
    private sealed class EmptyDirectory : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle handle) => null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => false;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    private sealed class NoGiver : IWeaponGiver
    {
        public bool GiveWeapon(EntityHandle player, string className) => false;
        public int GiveAmmo(EntityHandle player, int count, string ammoName) => 0;
    }

    /// <summary>
    /// The command, wired to a recorder instead of a queue. Cheats ON and an
    /// issuer present unless a test says otherwise, so every fact below is
    /// about the command's own behaviour rather than about its gate.
    /// </summary>
    private static (EntFireCommand Command, List<Posted> Posts) Rig(
        bool cheats = true, bool haveQueue = true, EntityHandle? issuer = null,
        bool dedicated = false, EntityHandle[]? slots = null)
    {
        var engine = Fake();
        var posts = new List<Posted>();

        // WHO IS IN WHICH SLOT, because the access rule is about slots. The
        // default puts the issuer in slot 1 -- the listen server host, which is
        // what every other fact here is about. A factory rather than a value
        // because RecordingEngine keys an answer on the MEMBER and not on the
        // argument, and this one has to differ per slot.
        var occupied = slots ?? [Player];
        int asked = 0;

        engine.Returns("Host.IsDedicatedServer", dedicated)
              .Returns("Players.GetHandle", object? () =>
              {
                  int slot = asked++;   // the walk asks 1, 2, 3 ... in order
                  return slot < occupied.Length ? occupied[slot] : EntityHandle.None;
              });

        var context = new DevContext(engine, engine, engine, engine,
                                     new NoGiver(), new EmptyDirectory(), new EntityFactories(), engine, new RecordingLog())
        {
            CheatsEnabled = () => cheats,
            Issuer = () => issuer ?? Player,
        };

        if (haveQueue)
        {
            context.PostInput = (target, input, value, delay, activator)
                => posts.Add(new Posted(target, input, value, delay, activator));
        }

        return (new EntFireCommand(context, engine), posts);
    }

    /// <summary>
    /// The command run with the console CAPTURED.
    ///
    /// <para>
    /// EVERY REFUSAL BELOW ASSERTS ON THE LINE, not merely on the absence of a
    /// post. Asserting "nothing was posted" alone is a check that CANNOT FAIL
    /// for the thing it is meant to catch: a refusal that returns in silence
    /// posts nothing either, and it is silence that this whole command is a fix
    /// for. Caught in the act — the fifth mutation in scratch/gz/mutate.py
    /// deleted the null-queue message and the suite stayed green until this
    /// existed.
    /// </para>
    /// </summary>
    private static (List<Posted> Posts, string Console) RunCapturing(
        EntFireCommand command, List<Posted> posts, string args)
    {
        // The command's own log, its context's (statics to zero).
        var log = (RecordingLog)command.Context.Log;
        int before = log.Lines.Count;
        command.Execute(args);

        return (posts, string.Concat(log.Lines.Skip(before).Select(l => l.Text)));
    }

    //-------------------------------------------------------------------------
    // THE DEFECT ITSELF: the command exists, and it posts.
    //-------------------------------------------------------------------------

    [Fact]
    public void EntFireIsARegisteredConsoleCommand()
    {
        var name = typeof(EntFireCommand)
            .GetCustomAttributes(typeof(ConCommandAttribute), false);

        Assert.Single(name);
        Assert.Equal("ent_fire", ((ConCommandAttribute)name[0]).Name);
    }

    [Fact]
    public void ATargetAndAnInputArePostedAsOneEvent()
    {
        var (command, posts) = Rig();

        command.Execute("gz_probe_a Kill");

        var only = Assert.Single(posts);
        Assert.Equal("gz_probe_a", only.Target);
        Assert.Equal("Kill", only.Input);
    }

    [Fact]
    public void TheTargetIsPassedTHROUGH_NotResolved()
    {
        // A classname, a wildcard and a procedural name all reach the queue
        // verbatim. The queue resolves all three (EntityDirectory.Deliver) and a
        // command that pre-resolved them would be a second implementation of
        // that, free to drift -- and would break !activator outright, which has
        // no meaning until the event is delivered.
        var (command, posts) = Rig();

        command.Execute("prop_physics Use");
        command.Execute("!activator Use");
        command.Execute("gz_* Use");

        Assert.Equal(new[] { "prop_physics", "!activator", "gz_*" },
                     posts.ConvertAll(p => p.Target).ToArray());
    }

    //-------------------------------------------------------------------------
    // STOCK'S DEFAULTS, each one separately.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheDefaultActionIsUse()
    {
        var (command, posts) = Rig();

        command.Execute("gz_probe_a");

        Assert.Equal("Use", Assert.Single(posts).Input);
    }

    [Fact]
    public void TheDefaultValueIsEmpty()
    {
        var (command, posts) = Rig();

        command.Execute("gz_probe_a Kill");

        Assert.Equal(string.Empty, Assert.Single(posts).Value);
    }

    [Fact]
    public void TheDefaultDelayIsZero()
    {
        var (command, posts) = Rig();

        command.Execute("gz_probe_a Kill");

        Assert.Equal(0f, Assert.Single(posts).Delay);
    }

    [Fact]
    public void AValueIsCarried()
    {
        var (command, posts) = Rig();

        command.Execute("gz_probe_b SetPosition 0.5");

        Assert.Equal("0.5", Assert.Single(posts).Value);
    }

    [Fact]
    public void AQuotedValueWithSpacesSurvivesAsOneArgument()
    {
        // THE ONE THAT MATTERS TO THE GATE. `debug-api connect` sends
        //   ent_fire <a> AddOutput "OnFullyOpen <b>:SetPosition:0.5:12:2"
        // and a naive space split would hand AddOutput the word "OnFullyOpen"
        // and drop the connection -- which is indistinguishable from the
        // command not existing, i.e. from the bug this file is about.
        var (command, posts) = Rig();

        command.Execute("gz_probe_a AddOutput \"OnFullyOpen gz_probe_b:SetPosition:0.5:12:2\"");

        var only = Assert.Single(posts);
        Assert.Equal("AddOutput", only.Input);
        Assert.Equal("OnFullyOpen gz_probe_b:SetPosition:0.5:12:2", only.Value);
    }

    //-------------------------------------------------------------------------
    // THE DELAY. Stock read it with atoi; this reads a float, and the
    // difference is a real one -- a map's connections have carried fractional
    // delays all along.
    //-------------------------------------------------------------------------

    [Fact]
    public void AWholeSecondDelayIsCarried()
    {
        var (command, posts) = Rig();

        command.Execute("gz_probe_a Open \"\" 12");

        Assert.Equal(12f, Assert.Single(posts).Delay);
    }

    [Fact]
    public void AFractionalDelayIsNotTruncatedToZero()
    {
        // atoi("0.5") is 0, which would be "immediately" -- a silent wrong
        // answer, which is the class of bug this whole command is a fix for.
        Assert.Equal(0.5f, EntFireCommand.ParseDelay("0.5"));
    }

    [Fact]
    public void ADelayThatIsNotANumberIsZero()
    {
        Assert.Equal(0f, EntFireCommand.ParseDelay("soon"));
    }

    [Fact]
    public void ANegativeDelayIsZeroBecauseAnEventDueInThePastIsDueNow()
    {
        Assert.Equal(0f, EntFireCommand.ParseDelay("-4"));
    }

    //-------------------------------------------------------------------------
    // THE ACTIVATOR, AND THE ISSUER RULE.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheIssuerIsTheActivator()
    {
        var (command, posts) = Rig();

        command.Execute("gz_probe_a Use");

        Assert.Equal(Player, Assert.Single(posts).Activator);
    }

    [Fact]
    public void ItStillPostsWithNobodyToIssueIt()
    {
        // Stock queued the event with a NULL pPlayer rather than refusing, and
        // the server console -- which is how every tool in this tree types --
        // never has a command client. Refusing here would make a headless
        // server unable to drive its own map, and ent_fire_access.h says so in
        // as many words: engine->ServerCommand on a LISTEN server is the host.
        var (command, posts) = Rig(issuer: EntityHandle.None);

        command.Execute("gz_probe_a Use");

        Assert.Equal(EntityHandle.None, Assert.Single(posts).Activator);
    }

    //-------------------------------------------------------------------------
    // WHO MAY FIRE -- the managed half of game/server/ent_fire_access.h, and
    // the same table its gtest pins. The rule came across with the command
    // because it is stock's point_servercommand escalation guard, and dropping
    // it silently is the thing this lane exists to stop happening.
    //-------------------------------------------------------------------------

    [Fact]
    public void AClientConsoleNamesItsOwnSlot()
    {
        Assert.Equal(4, EntFireAccess.IssuerSlot(4, dedicatedServer: false));
        Assert.Equal(4, EntFireAccess.IssuerSlot(4, dedicatedServer: true));
    }

    [Fact]
    public void AListenServerConsoleIsTheHost()
    {
        Assert.Equal(1, EntFireAccess.IssuerSlot(EntFireAccess.NoIssuer, dedicatedServer: false));
    }

    [Fact]
    public void ADedicatedServerConsoleIsNobody()
    {
        Assert.Equal(EntFireAccess.NoIssuer,
                     EntFireAccess.IssuerSlot(EntFireAccess.NoIssuer, dedicatedServer: true));
    }

    [Fact]
    public void NobodyMayNotFire()
    {
        Assert.False(EntFireAccess.SlotMayFire(EntFireAccess.NoIssuer, dedicatedServer: false));
    }

    [Fact]
    public void OnlyTheHostFiresOnAListenServer()
    {
        Assert.True(EntFireAccess.SlotMayFire(1, dedicatedServer: false));
        Assert.False(EntFireAccess.SlotMayFire(2, dedicatedServer: false));
    }

    [Fact]
    public void ADedicatedServerRefusesEveryone()
    {
        // The header asks about autokick there and nothing on the managed
        // surface can answer it -- and the C++ call site cannot either, because
        // it asks a CBasePlayer and this mod's player is never one. So this is
        // the behaviour the C++ has TODAY, not a new refusal.
        Assert.False(EntFireAccess.SlotMayFire(1, dedicatedServer: true));
        Assert.False(EntFireAccess.SlotMayFire(4, dedicatedServer: true));
    }

    [Fact]
    public void TheNullEqualsNullHoleIsClosed()
    {
        // Stock's listen-server half was `pPlayer != UTIL_GetListenServerHost()`
        // over two pointers that are BOTH NULL under SourceSharp, so the check
        // passed by accident and any client could fire. Slot 2 is refused here
        // and there is no arrangement of nulls that makes it pass.
        Assert.False(EntFireAccess.SlotMayFire(2, dedicatedServer: false));
        Assert.False(EntFireAccess.SlotMayFire(16, dedicatedServer: false));
    }

    [Fact]
    public void ANonHostClientIsRefusedAtTheCommand()
    {
        // Slot 1 is somebody else; the issuer is in slot 2. THE WHOLE POINT:
        //   give point_servercommand
        //   ent_fire point_servercommand command "rcon_password mynewpassword"
        var host = new EntityHandle(9u);
        var (command, posts) = Rig(issuer: Player, slots: [host, Player]);

        var (_, console) = RunCapturing(command, posts, "gz_probe_a Kill");

        Assert.Empty(posts);
        Assert.Contains("only the listen server host", console);
    }

    [Fact]
    public void ADedicatedServerConsoleIsRefusedAtTheCommand()
    {
        var (command, posts) = Rig(issuer: EntityHandle.None, dedicated: true);

        var (_, console) = RunCapturing(command, posts, "gz_probe_a Kill");

        Assert.Empty(posts);
        Assert.Contains("no console client issued this", console);
    }

    [Fact]
    public void TheHostIsNotRefused()
    {
        // The control for the two refusals above: the SAME command, the same
        // rig, an issuer in slot 1, and it posts.
        var (command, posts) = Rig(issuer: Player, slots: [Player]);

        command.Execute("gz_probe_a Kill");

        Assert.Single(posts);
    }

    //-------------------------------------------------------------------------
    // THE REFUSALS. Every one of them is a LINE, never a silence -- which is
    // the rule the missing command broke.
    //-------------------------------------------------------------------------

    [Fact]
    public void ItIsCheatGatedTheWayStocksFCVAR_CHEATWas()
    {
        var (command, posts) = Rig(cheats: false);

        var (_, console) = RunCapturing(command, posts, "gz_probe_a Kill");

        Assert.Empty(posts);
        Assert.Contains("sv_cheats", console);
    }

    [Fact]
    public void NoTargetPostsNothingAndSaysHowToUseIt()
    {
        var (command, posts) = Rig();

        var (_, console) = RunCapturing(command, posts, "");

        Assert.Empty(posts);
        Assert.Contains("usage: ent_fire <target> [action] [value] [delay]", console);
    }

    [Fact]
    public void WithNoQueueBehindItNothingIsPosted()
    {
        // The seam is null when nothing owns a queue. The command must not
        // throw and must not pretend.
        var (command, posts) = Rig(haveQueue: false);

        RunCapturing(command, posts, "gz_probe_a Kill");

        Assert.Empty(posts);
    }

    [Fact]
    public void WithNoQueueBehindItTheREFUSAL_IS_PRINTED()
    {
        // ITS OWN FACT, separate from the one above, and the separation is the
        // point: "nothing was posted" is true of a working refusal AND of a
        // silent return, so only this one can tell them apart.
        var (command, posts) = Rig(haveQueue: false);

        var (_, console) = RunCapturing(command, posts, "gz_probe_a Kill");

        Assert.Contains("ent_fire:", console);
        Assert.Contains("not posted", console);
    }

    [Fact]
    public void APOSTED_INPUT_SAYS_SO_TOO()
    {
        // The other half of the same rule: a command that worked is as
        // unreadable as one that did not if it prints nothing, and every other
        // command in this family prints a line.
        var (command, posts) = Rig();

        var (_, console) = RunCapturing(command, posts, "gz_probe_a Kill");

        Assert.Single(posts);
        Assert.Contains("Kill", console);
        Assert.Contains("gz_probe_a", console);
    }
}
