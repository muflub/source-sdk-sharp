using SourceSharp.Abi;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;
using System.Runtime.InteropServices;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The scoreboard header's player count (docs/todo.md, client follow-ups:
/// "1 / 33" on a listen server).
///
/// <para>
/// Stock's figure is <c>gpGlobals-&gt;maxClients</c> —
/// <c>ClientScoreBoardDialog.cpp:320</c> loops <c>1..gpGlobals-&gt;maxClients</c>
/// — not the player resource's array bound. The port showed <c>Slots - 1</c>
/// (33) instead, the <see cref="PlayerResourceNetState.Slots"/> array size less
/// the world row: a <c>maxplayers 8</c> listen server said "1 / 33" because 33
/// is what the array can hold, not what the server allows.
/// </para>
///
/// <para>
/// The read is the SAME LOAD stock does: <see cref="IEngineEnvironment.Globals"/>
/// reads this side's live <c>CGlobalVars</c> — the same memory
/// <c>gp_globals.h</c>'s <c>gpGlobals</c> is. The <c>GetMaxClients</c> vtable
/// slot stays unexposed: the refusal in
/// <c>tools/vtable-policy.json</c> names the globals load as the genuine second
/// source, and the second source is what this uses.
/// </para>
///
/// <para>
/// With no engine behind the read (a test process, or before the globals are
/// bound), the figure falls back to the array bound rather than 0: "1 / 0"
/// would be a worse lie than the ceiling.
/// </para>
/// </summary>
public class ScoreboardMaxPlayersTests
{
    private static nint EngineGlobals(int maxClients)
    {
        nint raw = Marshal.AllocHGlobal(Marshal.SizeOf<CGlobalVarsRaw>());
        for (int i = 0; i < Marshal.SizeOf<CGlobalVarsRaw>(); i++)
            Marshal.WriteByte(raw, i, 0);
        Marshal.WriteInt32(raw,
            (int)Marshal.OffsetOf<CGlobalVarsRaw>(nameof(CGlobalVarsRaw.maxClients)), maxClients);
        return raw;
    }

    // The environment enters the way every engine read enters a client UI
    // source: as a constructor service (plan_abi_hotreload D8 — injected, no
    // static). Reflection finds it so this file COMPILES against the base
    // build, where the parameter does not exist yet; the fact is then red at
    // the base, not a build failure (repo rule: the build compiles at every
    // commit, test-first commits included).
    private static ScoreboardUi Scores(RecordingEngine engine)
    {
        return new ScoreboardUi(new UiHostKit.NoStandalones(), engine, new DefaultForwarders.NoEngineClient(),
                                new EnginePlayerNames(new DefaultForwarders.NoEngineClient(), new IdentityNameFilter()), engine, engine);
    }

    [Fact]
    public void TheHeaderCountIsTheEnginesMaxClients()
    {
        var engine = TestServices.Fake().Returns("Console.FindConVarValue", "test server");
        engine.Returns("Environment.Globals", new Globals(EngineGlobals(maxClients: 8)));

        var scores = Scores(engine);
        scores.Refresh();

        Assert.Equal(8, scores.MaxPlayers);
    }

    [Fact]
    public void WithoutTheEngineReadTheCeilingStandsNotZero()
    {
        // The control arm, and the negative one: the globals pointer is not
        // bound (IsAvailable false — the state before the engine has answered
        // with one, and the state of a test process), so MaxClients cannot be
        // read at all. The ceiling, not a crash and not 0.
        var engine = TestServices.Fake().Returns("Console.FindConVarValue", "test server");
        engine.Returns("Environment.Globals", default(Globals));

        var scores = Scores(engine);
        scores.Refresh();

        Assert.Equal(PlayerResourceNetState.Slots - 1, scores.MaxPlayers);
    }

    [Fact]
    public void TheCountFollowsAMidGameMaxPlayersChange()
    {
        // The load is live, not latched at first Refresh: a listen server's
        // maxplayers change between maps must show up the next time the
        // scoreboard opens.
        var engine = TestServices.Fake().Returns("Console.FindConVarValue", "test server");
        nint eight = EngineGlobals(8);
        nint twelve = EngineGlobals(12);
        engine.Returns("Environment.Globals", new Globals(eight));
        var scores = Scores(engine);
        scores.Refresh();
        Assert.Equal(8, scores.MaxPlayers);

        engine.Returns("Environment.Globals", new Globals(twelve));
        scores.Refresh();

        Assert.Equal(12, scores.MaxPlayers);
    }
}
