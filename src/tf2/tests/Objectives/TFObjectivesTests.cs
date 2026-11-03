using System.Runtime.CompilerServices;

using SourceSharp.Abi;

using TF2.Server.Entities.Ctf;
using TF2.Server.Objectives;
using TF2.Shared;

using TF2.Tests.Ctf;
using TF2.Tests.Payload;

using Xunit;

namespace TF2.Tests.Objectives;

/// <summary>
/// The objectives feed (<see cref="TFObjectives"/>): the version bumps only on
/// a differing record, and an empty chain keeps <c>All</c> empty.
/// </summary>
public sealed class TFObjectivesTests
{
    /// <summary>The chain, in the order the sources were handed in (as <c>Hooks&lt;T&gt;</c> keeps them).</summary>
    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;

        public void Each(string what, Action<T> call)
        {
            foreach (var each in hooks)
                call(each);
        }
    }

    /// <summary>A chain member answering whatever its list holds at the refresh.</summary>
    private sealed class Source(params TFObjective[] records) : ITFObjectiveSource
    {
        public readonly List<TFObjective> Records = [.. records];

        public void Collect(List<TFObjective> into) => into.AddRange(Records);
    }

    private static TFObjective Make(int id, TFObjectiveKind kind, TFTeam owner, float progress = 0f)
        => new(id, kind, owner, TFTeam.Unassigned, TFObjectiveState.Open, new Vector(1f, 2f, 3f), progress,
               CarrierIndex: 0, UnlockIn: 0f, TimerLeft: 0f);

    [Fact]
    public void AFrameNothingMovedInKeepsItsArray()
    {
        var source = new Source(Make(1, TFObjectiveKind.CaptureZone, TFTeam.Red, progress: 0.4f));
        var feed = new TFObjectives(new ListHooks<ITFObjectiveSource>(source));
        feed.Refresh();
        ref readonly TFObjective first = ref feed.All[0];

        feed.Refresh();
        Assert.True(Unsafe.AreSame(in first, in feed.All[0]));

        source.Records[0] = Make(1, TFObjectiveKind.CaptureZone, TFTeam.Red, progress: 0.9f);
        feed.Refresh();
        Assert.False(Unsafe.AreSame(in first, in feed.All[0]));
    }

    [Fact]
    public void VersionBumpsOnlyWhenARecordChanges()
    {
        var source = new Source(Make(1, TFObjectiveKind.CaptureZone, TFTeam.Red, progress: 0.4f));
        var feed = new TFObjectives(new ListHooks<ITFObjectiveSource>(source));

        Assert.Equal(0, feed.Version);

        feed.Refresh();
        Assert.Equal(1, feed.Version);
        Assert.Equal(1, feed.All.Length);

        // the same records again: nothing bumped.
        feed.Refresh();
        Assert.Equal(1, feed.Version);

        source.Records[0] = Make(1, TFObjectiveKind.CaptureZone, TFTeam.Red, progress: 0.9f);
        feed.Refresh();
        Assert.Equal(2, feed.Version);

        feed.Refresh();
        Assert.Equal(2, feed.Version);
    }

    [Fact]
    public void AllIsEmptyWithNoSources()
    {
        var feed = new TFObjectives(new ListHooks<ITFObjectiveSource>());

        Assert.True(feed.All.IsEmpty);

        feed.Refresh();
        feed.Refresh();

        Assert.True(feed.All.IsEmpty);
        Assert.Equal(0, feed.Version);
    }

    /// <summary>The CTF feed over a rig: one chain member, the real directory.</summary>
    private static TFObjectives CtfFeed(CtfRig rig)
        => new(new ListHooks<ITFObjectiveSource>(new TF2CtfObjectiveSource(rig.Registry, rig.Engine, rig.Clock)));

    private static TFObjective Record(ReadOnlySpan<TFObjective> all, TFObjectiveKind kind)
    {
        foreach (TFObjective record in all)
        {
            if (record.Kind == kind)
                return record;
        }

        throw new InvalidOperationException($"no {kind} record in the feed");
    }

    /// <summary>
    /// A stolen flag rides as Carried with its taker's index, the flag's own
    /// team as owner and the enemy as the team that may act; a capture zone
    /// stands beside it, Open, owned by its keyed team.
    /// </summary>
    [Fact]
    public void AStolenFlagIsCarriedByItsTaker()
    {
        var rig = new CtfRig();
        var blueFlag = rig.Flag(TFTeam.Blue);
        EntityHandle redPlayer = rig.Players.Add(1, TFTeam.Red);
        rig.Zone(TFTeam.Red);
        blueFlag.PickUp(redPlayer, invisible: true);

        var feed = CtfFeed(rig);
        feed.Refresh();

        Assert.Equal(2, feed.All.Length);

        TFObjective flag = Record(feed.All, TFObjectiveKind.Flag);
        Assert.Equal(blueFlag.Handle.EntryIndex, flag.Id);
        Assert.Equal(TFTeam.Blue, flag.Owner);
        Assert.Equal(TFTeam.Red, flag.MayAct);
        Assert.Equal(TFObjectiveState.Carried, flag.State);
        Assert.Equal(1, flag.CarrierIndex);
        Assert.Equal(CtfRig.BlueHome, flag.Position);
        Assert.Equal(1f, flag.Progress);

        TFObjective zone = Record(feed.All, TFObjectiveKind.CaptureZone);
        Assert.Equal(TFTeam.Red, zone.Owner);
        Assert.Equal(TFTeam.Unassigned, zone.MayAct);
        Assert.Equal(TFObjectiveState.Open, zone.State);
        Assert.Equal(1f, zone.Progress);
    }

    /// <summary>
    /// The red player taking the blue flag and capping it moves only the red
    /// team's last-progress ticks, at the refresh that saw each gain.
    /// </summary>
    [Fact]
    public void ACaptureMovesTheTeamsLastProgress()
    {
        var rig = new CtfRig();
        var blueFlag = rig.Flag(TFTeam.Blue);
        EntityHandle redPlayer = rig.Players.Add(1, TFTeam.Red);
        rig.Zone(TFTeam.Red);

        var feed = CtfFeed(rig);
        feed.Refresh();

        Assert.Equal(0, feed.LastProgressTick(TFTeam.Red));

        blueFlag.PickUp(redPlayer, invisible: true);
        feed.Refresh();
        Assert.Equal(2, feed.LastProgressTick(TFTeam.Red));

        blueFlag.Capture(redPlayer, capturePoint: 1);
        feed.Refresh();

        Assert.Equal(3, feed.LastProgressTick(TFTeam.Red));
        Assert.Equal(0, feed.LastProgressTick(TFTeam.Blue));
    }

    /// <summary>A flag nobody touches never moves either team's last-progress ticks.</summary>
    [Fact]
    public void AnUntouchedFlagLeavesLastProgressAlone()
    {
        var rig = new CtfRig();
        rig.Flag(TFTeam.Blue);
        rig.Players.Add(1, TFTeam.Red);
        rig.Zone(TFTeam.Red);

        var feed = CtfFeed(rig);
        feed.Refresh();
        feed.Refresh();

        Assert.Equal(0, feed.LastProgressTick(TFTeam.Red));
        Assert.Equal(0, feed.LastProgressTick(TFTeam.Blue));
    }

    /// <summary>The payload feed over a rig: one chain member, the real directory.</summary>
    private static TFObjectives PayloadFeed(PayloadRig rig)
        => new(new ListHooks<ITFObjectiveSource>(new TF2PayloadObjectiveSource(rig.Registry, rig.Engine)));

    /// <summary>
    /// The cart's path fraction rising is the pushing team's gain: the refresh
    /// that sees the advance moves that team's last-progress tick, and no
    /// other team's.
    /// </summary>
    [Fact]
    public void ACartAdvanceMovesTheAttackersLastProgress()
    {
        var (rig, watcher, train, _, _, _) = TeamTrainWatcherTests.Track();

        var feed = PayloadFeed(rig);
        feed.Refresh();

        Assert.Equal(0, feed.LastProgressTick(TFTeam.Blue));

        // the cart rides to n2: half the track done.
        rig.EngineOrigin = new Vector(500f, 0f, 0f);
        train.FinishStep();
        rig.ThinkAt(watcher, 0.1f);

        feed.Refresh();

        Assert.Equal(2, feed.LastProgressTick(TFTeam.Blue));
        Assert.Equal(0, feed.LastProgressTick(TFTeam.Red));
    }

    /// <summary>
    /// After the cappers leave and the recede window passes, the cart's row
    /// reads RollingBack, owned by (and open to) the watcher's own team, at
    /// the cart's place.
    /// </summary>
    [Fact]
    public void ARollingBackCartIsRollingBack()
    {
        var (rig, watcher, train, _, _, _) = TeamTrainWatcherTests.Track();
        var area = rig.Area("cp_a");

        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "2", 2f, area.Handle);
        train.FinishStep();
        rig.ThinkAt(watcher, 0.1f);
        rig.Registry.Input(watcher.Handle, "SetNumTrainCappers", "0", 0f, area.Handle);
        rig.ThinkAt(watcher, 0.3f);
        rig.ThinkAt(watcher, 0.1f + watcher.GetRecedeTotalTime() + 0.1f);

        var feed = PayloadFeed(rig);
        feed.Refresh();

        Assert.Equal(1, feed.All.Length);

        TFObjective cart = Record(feed.All, TFObjectiveKind.Cart);
        Assert.Equal(watcher.Handle.EntryIndex, cart.Id);
        Assert.Equal(TFTeam.Blue, cart.Owner);
        Assert.Equal(TFTeam.Blue, cart.MayAct);
        Assert.Equal(TFObjectiveState.RollingBack, cart.State);
        Assert.Equal(new Vector(100f, 200f, 300f), cart.Position);
    }
}
