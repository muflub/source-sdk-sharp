/*
 * The build stamp in the HUD corner.
 *
 * What can go wrong here is all silence: a corner that shows a confident
 * "unknown", a toggle that does not toggle, and a stamp that stays put while the
 * build moves. Each is provoked. Where it sits is the toolkit's (BuildStampPanel,
 * plan_client_ui U2): its goldens and the lint hold that.
 */

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ui;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class HudBuildStampTests
{
    private static readonly DateTime Noon = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static BuildHalf Half(string hash, DateTime built, string branch = "example-trunk-branch")
        => new(new BuildStamp(hash, built, 4),
               new GitProvenance(branch, "45e801f2", false), "/mod/bin", false);

    /// <summary>
    /// A stamp wired to a table this test owns. Nothing here reads the
    /// process-wide registry, which two suites running in the same process would
    /// otherwise share.
    /// </summary>
    private static HudBuildStamp Stamp(RecordingEngine engine, BuildHalf native, BuildHalf managed,
                                       BuildFreshness nativeFresh = BuildFreshness.Current,
                                       BuildFreshness managedFresh = BuildFreshness.Current)
        => new(Measuring(engine), new BuildInfo())
        {
            Native = _ => native,
            Managed = _ => managed,
            NativeFreshness = _ => nativeFresh,
            ManagedFreshness = _ => managedFresh,
        };

    /// <summary>
    /// An engine that answers the one question this class asks of it. Fifteen
    /// pixels a character and twenty tall is roughly what the scheme's font
    /// measures — the numbers matter only in that they are not the eight this
    /// used to assume, which is what put the first version half off-screen.
    /// </summary>
    private static RecordingEngine Measuring(RecordingEngine engine)
    {
        engine.Returns("Hud.MeasureText", new TextSize(620, 20));
        return engine;
    }

    /// <summary>What the stamp shows once refreshed: its line, or nothing when it is off.</summary>
    private static IEnumerable<string> Said(HudBuildStamp stamp)
    {
        stamp.Refresh(1.0);
        IBuildStampSource source = stamp;
        if (source.Visible && source.Line.Length > 0)
            yield return source.Line;
        if (source.Visible && source.DebugLine.Length > 0)
            yield return source.DebugLine;
    }

    //-------------------------------------------------------------------------
    // What it says.
    //-------------------------------------------------------------------------

    [Fact]
    public void BothHalvesAreDrawnBecauseTheyDiverge()
    {
        var engine = Fake();

        var stamp = Stamp(engine, Half("aaaaaaaa", Noon), Half("bbbbbbbb", Noon.AddMinutes(90)));

        var lines = Said(stamp).ToList();

        Assert.True(lines.Count == 1, "both halves share one compact line");
        Assert.True(lines[0].StartsWith("n "), "native first");
        Assert.True(lines[0].Contains(" · m "), "then managed");
        Assert.True(lines[0].Contains("aaaa") && lines[0].Contains("bbbb"),
            "a hot reload moves one half and not the other, so each carries its own hash");
        Assert.True(!lines[0].Contains("aaaaa"), $"cut to {HudBuildStamp.HashChars} characters");
    }

    [Fact]
    public void TheLineCarriesTimeHashAndBranch()
    {
        var engine = Fake();

        var stamp = Stamp(engine, Half("aaaaaaaa", Noon, "example-feature-a1b2c3"),
              Half("bbbbbbbb", Noon, "example-feature-a1b2c3"));

        string line = Said(stamp).Single();

        Assert.True(line.Contains($"{Noon.ToLocalTime():HH:mm:ss}"), "when it was built");
        Assert.True(line.Contains("bbbb"), "what was built");
        Assert.True(line.EndsWith("example-feature-a1b2c3@45e801f2"),
            "and which branch it came from, once, because several worktrees' games are up at once");
        Assert.True(line.IndexOf("@45e801f2") == line.LastIndexOf("@45e801f2"),
            "a source both halves share is not said twice");
    }

    [Fact]
    public void HalvesFromDifferentSourcesEachSayTheirs()
    {
        var engine = Fake();

        var stamp = Stamp(engine, Half("aaaaaaaa", Noon, "branch-one"), Half("bbbbbbbb", Noon, "branch-two"));

        string line = Said(stamp).Single();

        Assert.True(line.Contains("branch-one@") && line.Contains("branch-two@"),
            "when the halves came from different trees, the line says so");
    }

    [Fact]
    public void AHalfThatWasNeverStampedDrawsNothing()
    {
        var engine = Fake();

        var stamp = Stamp(engine, BuildHalf.Unknown, Half("bbbbbbbb", Noon));

        string line = Said(stamp).Single();

        Assert.True(!line.Contains("n "), "a corner reading 'unknown' looks like a reading, so there is none");
        Assert.True(line.StartsWith("m "), "and the half that IS known still says so");
    }

    //-------------------------------------------------------------------------
    // The warning, which is the part that has to be right about which half.
    //-------------------------------------------------------------------------

    [Fact]
    public void ARebuiltManagedHalfIsToldToReload()
    {
        var engine = Fake();

        var stamp = Stamp(engine, Half("aaaaaaaa", Noon), Half("bbbbbbbb", Noon),
              managedFresh: BuildFreshness.Stale);

        string line = Said(stamp).Single();
        string native = line[..line.IndexOf(" · m")];
        string managed = line[line.IndexOf(" · m")..];

        Assert.True(managed.Contains("cs_cl_reload"),
            "the managed half can be replaced in place, so that is what it says to do");
        Assert.True(managed.IndexOf("REBUILT") < managed.IndexOf("bbbb"),
            "and says it before its stamp, where clipping cannot take it");
        Assert.True(!native.Contains("REBUILT"), "and the native half is not accused of it");
    }

    [Fact]
    public void ARebuiltNativeHalfIsToldToRestart()
    {
        var engine = Fake();

        var stamp = Stamp(engine, Half("aaaaaaaa", Noon), Half("bbbbbbbb", Noon),
              nativeFresh: BuildFreshness.Stale);

        string line = Said(stamp).Single();
        string native = line[..line.IndexOf(" · m")];

        Assert.True(native.Contains("restart"),
            "native cannot be reloaded into a running process, so a reload would be the wrong advice");
        Assert.True(!native.Contains("cs_cl_reload"), "and is not offered");
    }

    [Fact]
    public void NothingRebuiltSaysNothing()
    {
        var engine = Fake();

        var stamp = Stamp(engine, Half("aaaaaaaa", Noon), Half("bbbbbbbb", Noon));

        Assert.True(Said(stamp).All(line => !line.Contains("REBUILT")),
            "a stamp that always warned would be a stamp nobody read");
    }

    [Fact]
    public void ARebuildIsLoudEnoughToNotice()
    {
        // The panel draws a stale stamp in the accent tone and the rest muted;
        // what it reads is whether either half is stale.
        IBuildStampSource stale = Stamp(Fake(), Half("aaaaaaaa", Noon), Half("bbbbbbbb", Noon), managedFresh: BuildFreshness.Stale);
        IBuildStampSource current = Stamp(Fake(), Half("aaaaaaaa", Noon), Half("bbbbbbbb", Noon));
        IBuildStampSource unknown = Stamp(Fake(), Half("aaaaaaaa", Noon), Half("bbbbbbbb", Noon), nativeFresh: BuildFreshness.Unknown);
        foreach (var s in new[] { stale, current, unknown })
            ((HudBuildStamp)s).Refresh(1.0);

        Assert.True(stale.Stale, "the one line worth looking at is the one that is not dim");
        Assert.False(current.Stale, "and the rest sits quietly, because it is on screen the whole time");
        Assert.False(unknown.Stale, "not knowing is not an alarm");
    }

    [Fact]
    public void TheDebugLineSaysWhenAndWhere()
    {
        string line = ViewReadout.Line(123.4567f, new Vector(-3843.25f, 2894.5f, 64.03f), 25f, 90f);

        Assert.Equal("t 123.457  pos -3843.2 2894.5 64.0  ang 25.0 90.0", line);
    }

    [Fact]
    public void NoDebugLineTakesNoRoom()
    {
        var engine = Fake();

        var stamp = Stamp(engine, Half("aaaaaaaa", Noon), Half("bbbbbbbb", Noon));

        Assert.True(Said(stamp).Count() == 1, "without a readout only the stamp is shown");
    }

    //-------------------------------------------------------------------------
    // The toggle.
    //-------------------------------------------------------------------------

    [Fact]
    public void TurningItOffDrawsNothingAtAll()
    {
        var engine = Fake();

        var stamp = Stamp(engine, Half("aaaaaaaa", Noon), Half("bbbbbbbb", Noon));
        stamp.Visible = () => false;

        Assert.True(!Said(stamp).Any(), "cs_cl_build_stamp 0 means an empty corner");
    }

    [Fact]
    public void AStampNobodyWiredIsStillOnScreen()
    {
        var engine = Fake();

        // The default, not the wiring. On by default is the decision this pins:
        // the failure being prevented is somebody NOT thinking to ask which
        // build it is, and a stamp they had to enable would not have been there.
        var stamp = new HudBuildStamp(Measuring(engine), new BuildInfo())
        {
            Native = _ => Half("aaaaaaaa", Noon),
            Managed = _ => Half("bbbbbbbb", Noon),
            NativeFreshness = _ => BuildFreshness.Current,
            ManagedFreshness = _ => BuildFreshness.Current,
        };

        Assert.True(Said(stamp).Count() == 1, "the default is to show it");
    }

    //-------------------------------------------------------------------------
    // The whole point: it moves when the build moves.
    //-------------------------------------------------------------------------

    [Fact]
    public void AReloadedManagedHalfReadsDifferentlyFromTheOneBeforeIt()
    {
        var before = Fake();
        var after = Fake();

        var native = Half("aaaaaaaa", Noon);

        var was0 = Stamp(before, native, Half("bbbbbbbb", Noon));
        var now0 = Stamp(after,  native, Half("cccccccc", Noon.AddMinutes(87)));

        static (string Native, string Managed) Split(string line)
            => (line[..line.IndexOf(" · m")], line[line.IndexOf(" · m")..]);

        var was = Split(Said(was0).Single());
        var now = Split(Said(now0).Single());

        Assert.True(was.Managed != now.Managed,
            "a managed half rebuilt and reloaded does not read as the one it replaced");
        Assert.True(was.Native == now.Native,
            "while native, which the reload did not touch, reads exactly as it did");
    }
}
