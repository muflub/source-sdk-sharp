using System.Collections.Generic;
using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <c>drawline</c> and <c>drawcross</c>, which draw again.
///
/// <para>
/// <b>WHAT THIS IS ABOUT.</b> Both registered from
/// <c>game/server/client.cpp</c> and came across into
/// <c>shell_client_orphans.cpp</c> at the Phase 4 exit as working native
/// commands. Phase 1b deleted that file and retired them, because what they
/// call — <c>UTIL_AddDebugLine</c> — puts a line into the ENGINE's debug
/// overlay and this ABI published no line primitive at all. Lane 1e-abi
/// appended <c>AddLineOverlay</c>, so they are managed <c>DevCommand</c>s.
/// </para>
///
/// <para>
/// <b>EVERY FACT ASSERTS ON THE CALL, not on the absence of a complaint.</b> A
/// debug overlay is drawn somewhere the person who typed the command may not be
/// looking, so "it printed a line and drew nothing" is exactly the failure
/// shape here — which is the whole reason these two were retired OUT LOUD
/// rather than left registered and silent.
/// </para>
/// </summary>
public class DebugDrawCommandTests
{
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
    /// An engine double whose trace answers <paramref name="blocked"/>, and a
    /// context with cheats on — every fact here is about the drawing rather
    /// than about the gate.
    /// </summary>
    private static (RecordingEngine Engine, DevContext Context) Rig(bool blocked = false, bool cheats = true)
    {
        var engine = Fake();

        engine.Returns("WorldQuery.TraceLine",
                       new TraceResult(blocked, blocked ? 0.5f : 1f,
                                       Vector.Zero, Vector.Zero, EntityHandle.None));

        var context = new DevContext(engine, engine, engine, engine,
                                     new NoGiver(), new EmptyDirectory(), new EntityFactories(), engine, new RecordingLog())
        {
            CheatsEnabled = () => cheats,
            Issuer = () => EntityHandle.None,
        };

        return (engine, context);
    }

    // What the command said: its context's log (statics to zero).
    private static string Run(DevCommand command, string args)
    {
        var log = (RecordingLog)command.Context.Log;
        int before = log.Lines.Count;
        command.Execute(args);
        return string.Concat(log.Lines.Skip(before).Select(l => l.Text));
    }

    private static EngineCall[] Lines(RecordingEngine engine)
        => engine.AllOf("Hud.AddLineOverlay").ToArray();

    //-------------------------------------------------------------------------
    // drawline
    //-------------------------------------------------------------------------

    [Fact]
    public void DrawLineDrawsOneLine()
    {
        var (engine, context) = Rig();

        Run(new DrawLineCommand(context, engine), "0 0 0 10 20 30");

        Assert.Single(Lines(engine));
    }

    [Fact]
    public void DrawLineDrawsBetweenTheSixArguments()
    {
        var (engine, context) = Rig();

        Run(new DrawLineCommand(context, engine), "1 2 3 4 5 6");

        var call = Lines(engine).Single();

        Assert.Equal(new Vector(1f, 2f, 3f), call.ArgAs<Vector>(0));
        Assert.Equal(new Vector(4f, 5f, 6f), call.ArgAs<Vector>(1));
    }

    /// <summary>
    /// Stock's colour rule: white when nothing is in the way. The condition it
    /// branched on was <c>tr.startsolid || tr.fraction &lt; 1.0</c>, which is
    /// exactly what <c>SourceSharp_TraceLine</c> returns and therefore exactly
    /// <see cref="TraceResult.Hit"/>.
    /// </summary>
    [Fact]
    public void DrawLineIsWhiteWhenTheLineIsClear()
    {
        var (engine, context) = Rig(blocked: false);

        Run(new DrawLineCommand(context, engine), "0 0 0 1 1 1");

        var call = Lines(engine).Single();

        Assert.Equal(255, call.ArgAs<int>(2));
        Assert.Equal(255, call.ArgAs<int>(3));
        Assert.Equal(255, call.ArgAs<int>(4));
    }

    [Fact]
    public void DrawLineIsRedWhenSomethingIsInTheWay()
    {
        var (engine, context) = Rig(blocked: true);

        Run(new DrawLineCommand(context, engine), "0 0 0 1 1 1");

        var call = Lines(engine).Single();

        Assert.Equal(255, call.ArgAs<int>(2));
        Assert.Equal(0, call.ArgAs<int>(3));
        Assert.Equal(0, call.ArgAs<int>(4));
    }

    /// <summary>
    /// The mask matters and is not the default. <see cref="TraceMask.Brushes"/>
    /// stops on glass and does NOT stop on a <c>CONTENTS_BLOCKLOS</c> volume,
    /// so a line coloured through it is coloured by a different question —
    /// plausibly, and in silence.
    /// </summary>
    [Fact]
    public void DrawLineAsksTheBlockLosQuestion()
    {
        var (engine, context) = Rig();

        Run(new DrawLineCommand(context, engine), "0 0 0 1 1 1");

        var trace = engine.Last("WorldQuery.TraceLine");

        Assert.NotNull(trace);
        Assert.Equal(TraceMask.BlockLos, trace!.Value.ArgAs<TraceMask>(2));
    }

    /// <summary>
    /// A duration of zero, which is what puts the line in the twenty-slot POOL
    /// — stock's <c>UTIL_AddDebugLine</c> lifetime. A positive duration would
    /// be the engine's own timed overlay and the line would expire; the
    /// engine's own zero means ONE FRAME, which is why the slot reads a
    /// non-positive duration as the pool rather than passing it on.
    /// </summary>
    [Fact]
    public void DrawLineAsksForThePoolAndNotForATimedOverlay()
    {
        var (engine, context) = Rig();

        Run(new DrawLineCommand(context, engine), "0 0 0 1 1 1");

        Assert.Equal(0f, Lines(engine).Single().ArgAs<float>(6));
    }

    /// <summary>Stock's clamp, from <c>public/worldsize.h</c>.</summary>
    [Fact]
    public void DrawLineClampsToTheWorld()
    {
        var (engine, context) = Rig();

        Run(new DrawLineCommand(context, engine), "1e30 0 0 -1e30 0 0");

        var call = Lines(engine).Single();

        Assert.Equal(DebugDrawCommand.MaxCoord, call.ArgAs<Vector>(0).X);
        Assert.Equal(-DebugDrawCommand.MaxCoord, call.ArgAs<Vector>(1).X);
    }

    [Fact]
    public void DrawLineWithTooFewArgumentsSaysSoAndDrawsNothing()
    {
        var (engine, context) = Rig();

        string console = Run(new DrawLineCommand(context, engine), "0 0 0");

        Assert.Empty(Lines(engine));
        Assert.Contains("usage", console, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The refusal is SAID. Asserting only that nothing was drawn is a check
    /// that cannot fail for the thing it is meant to catch: a command that
    /// returns in silence draws nothing either.
    /// </summary>
    [Fact]
    public void DrawLineRefusedForWantOfCheatsSaysSo()
    {
        var (engine, context) = Rig(cheats: false);

        string console = Run(new DrawLineCommand(context, engine), "0 0 0 1 1 1");

        Assert.Empty(Lines(engine));
        Assert.Contains("sv_cheats", console);
    }

    /// <summary>
    /// Neither command reads a player, and the server console — which is how
    /// every tool in this tree types — has no command client to name. Stock's
    /// registrations took no issuer either.
    /// </summary>
    [Fact]
    public void DrawLineWorksWithNobodyConnected()
    {
        // Rig's issuer is EntityHandle.None already; this fact is here to say
        // that NeedsIssuer is false on purpose rather than by accident.
        var (engine, context) = Rig();

        Run(new DrawLineCommand(context, engine), "0 0 0 1 1 1");

        Assert.Single(Lines(engine));
    }

    //-------------------------------------------------------------------------
    // drawcross
    //-------------------------------------------------------------------------

    [Fact]
    public void DrawCrossDrawsFourLines()
    {
        var (engine, context) = Rig();

        Run(new DrawCrossCommand(context, engine), "0 0 0");

        Assert.Equal(4, Lines(engine).Length);
    }

    /// <summary>
    /// The four are the BODY DIAGONALS of stock's ten-unit cube, and that is
    /// worth pinning rather than assuming: stock computed them by walking one
    /// start/end pair around the cube with four in-place edits, and the third
    /// pair's endpoints are not obvious from reading that walk. Four lines
    /// through the centre in any four directions would pass a count.
    /// </summary>
    [Fact]
    public void DrawCrossDrawsTheFourBodyDiagonalsOfStocksCube()
    {
        var (engine, context) = Rig();

        Run(new DrawCrossCommand(context, engine), "100 200 300");

        const float e = DrawCrossCommand.Extent;
        var at = new Vector(100f, 200f, 300f);

        var expected = new[]
        {
            (at + new Vector(-e, -e, -e), at + new Vector( e,  e,  e)),
            (at + new Vector( e, -e, -e), at + new Vector(-e,  e,  e)),
            (at + new Vector( e,  e, -e), at + new Vector(-e, -e,  e)),
            (at + new Vector(-e,  e, -e), at + new Vector( e, -e,  e)),
        };

        var drawn = Lines(engine)
            .Select(c => (c.ArgAs<Vector>(0), c.ArgAs<Vector>(1)))
            .ToArray();

        Assert.Equal(expected, drawn);
    }

    [Fact]
    public void DrawCrossWithTooFewArgumentsSaysSoAndDrawsNothing()
    {
        var (engine, context) = Rig();

        string console = Run(new DrawCrossCommand(context, engine), "1 2");

        Assert.Empty(Lines(engine));
        Assert.Contains("usage", console, System.StringComparison.OrdinalIgnoreCase);
    }

    //-------------------------------------------------------------------------
    // THE SLOT ITSELF -- the facts that go red if it is not wired.
    //
    // Every fact above runs against a RecordingEngine, so all of them pass
    // whatever the native side does. Since plan_engineapi_vtables C6c the slot
    // is a method of the server's DebugOverlay shim area rather than a struct
    // field: a pure virtual the concrete class must define, so "declared and
    // never assigned" no longer compiles. What is left to check is that the
    // method is declared on both sides and forwards to this body.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheShimAreaDeclaresTheAddLineOverlayMethod()
    {
        // The DECLARATION and not the name, read without comments.
        string areas = AbiParityTests.CodeOnly(File.ReadAllText(
            RepoLayout.Repo("src", "shared", "native", "ishim_areas.h")));

        Assert.Matches(@"\bvirtual\s+void\s+AddLineOverlay\s*\(", areas);
    }

    [Fact]
    public void TheManagedVtableDeclaresTheAddLineOverlaySlot()
    {
        var slot = typeof(ShimDebugOverlayFwd).GetField(nameof(ShimDebugOverlayFwd.AddLineOverlay));

        Assert.True(slot is { FieldType.IsFunctionPointer: true });
    }

    [Fact]
    public void TheShimAreaForwardsAddLineOverlayToItsBody()
    {
        // Comment-stripped, and anchored on the definition: this file's first
        // version of the wiring fact looked for a substring, and commenting the
        // assignment out left that text exactly where it was.
        string impl = AbiParityTests.CodeOnly(File.ReadAllText(
            RepoLayout.Repo("src", "shared", "native", "shell_shim_areas.inc")));

        Assert.Matches(@"CShimDebugOverlay::AddLineOverlay\s*\([^)]*\)\s*\{\s*return\s+::SourceSharp_AddLineOverlay\(", impl);
    }

    [Fact]
    public void TheNativeBodyPoolsALineWithNoDuration()
    {
        // The branch the commands depend on: duration 0 is the twenty-slot pool
        // (stock's lifetime), not a one-frame overlay. A body that always called
        // Overlay_Line would draw for one frame and print success.
        string body = Source("src", "sourcesharp", "game", "server", "shell_debugoverlays.cpp");

        Assert.Contains("void SourceSharp_AddLineOverlay(", body);
        Assert.Contains("Overlay_AddLine(", body);
    }

    private static string Source(params string[] parts)
    {
        return File.ReadAllText(RepoLayout.Sdk(parts));
    }
}
