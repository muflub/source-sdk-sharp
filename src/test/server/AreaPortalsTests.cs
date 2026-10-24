using System;
using System.Collections.Generic;
using System.Linq;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_native_v2 T4d: the area portal records in managed code. Every
/// fact is the native code it replaces, read before the move:
///
/// <list type="bullet">
/// <item>areaportal_list.h: a record starts open, not a window; tokens count
/// from 1 and 0xFFFFFFFF wraps to 1 (0 is "no portal");
/// ClosedByManagedState closes a shut portal, and a window whose viewer is
/// further than fade + 10 (AREAPORTAL_FADE_DIST_BUFFER) from its volume, the
/// distance scaled by the FOV factor, both also closed on the client.</item>
/// <item>shell_areaportal.cpp: Create refuses a negative number and opens the
/// portal in the engine; Destroy reopens it, then forgets it; SetOpen writes
/// the record and the engine; UpdateVisibility asks GetAreaPortalPlane (open
/// when the engine has no plane) and is open when the viewer is in front of
/// it, DistTo + 80 (AREAPORTAL_VIEWER_PADDING) &gt; 0.</item>
/// <item>ClientSetupVisibility's walk (shell_gameclients.cpp): records in
/// creation order, SetAreaPortalStates in batches of 512 and once more for the
/// rest (even none), a version-0 portal always open on the client, and a
/// portal number past the bit array an Error.</item>
/// </list>
/// </summary>
public sealed class AreaPortalsTests
{
    private sealed class Engine : IAreaPortalEngine
    {
        public readonly List<string> States = new();
        public readonly List<(int[] Numbers, int[] Open)> Batches = new();
        public readonly Dictionary<int, (Vector Normal, float Dist)> Planes = new();

        public void SetAreaPortalState(int portalNumber, bool open) => States.Add($"{portalNumber}={(open ? 1 : 0)}");

        public void SetAreaPortalStates(ReadOnlySpan<int> portalNumbers, ReadOnlySpan<int> isOpen)
            => Batches.Add((portalNumbers.ToArray(), isOpen.ToArray()));

        public bool GetAreaPortalPlane(Vector viewOrigin, int portalKey, out Vector normal, out float distance)
        {
            bool found = Planes.TryGetValue(portalKey, out var plane);
            (normal, distance) = plane;
            return found;
        }
    }

    private readonly Engine _engine = new();
    private readonly AreaPortals _portals;

    public AreaPortalsTests() => _portals = new AreaPortals(_engine);

    private byte[] Walk(Vector origin, float fov = 1f)
    {
        var bits = new byte[24];
        Assert.Equal(-1, _portals.Visibility(origin, fov, bits));
        return bits;
    }

    private static bool Bit(byte[] bits, int n) => (bits[n >> 3] & (1 << (n & 7))) != 0;

    // ---- the records --------------------------------------------------------

    [Fact]
    public void ANegativePortalNumberGetsNoRecordAndTheEngineIsNotAsked()
    {
        Assert.Equal(0u, _portals.Create(-1, 1));
        Assert.Empty(_engine.States);
    }

    [Fact]
    public void ARecordIsOpenedInTheEngineAndTokensCountFromOne()
    {
        Assert.Equal(1u, _portals.Create(3, 1));
        Assert.Equal(2u, _portals.Create(5, 1));
        Assert.Equal(new[] { "3=1", "5=1" }, _engine.States);
    }

    [Theory]
    [InlineData(0u, 1u)]
    [InlineData(1u, 2u)]
    [InlineData(0xFFFFFFFEu, 0xFFFFFFFFu)]
    [InlineData(0xFFFFFFFFu, 1u)]
    public void TheTokenAfterTheLastWrapsPastZero(uint token, uint next)
        => Assert.Equal(next, AreaPortals.TokenAfter(token));

    [Fact]
    public void ShuttingWritesTheEngineAndTheWalkSeesItShut()
    {
        uint p = _portals.Create(3, 1);
        _portals.SetOpen(p, false);

        byte[] bits = Walk(Vector.Zero);

        Assert.Equal(new[] { "3=1", "3=0" }, _engine.States);
        Assert.Equal(new[] { 0 }, _engine.Batches.Single().Open);
        Assert.False(Bit(bits, 3), "a shut portal is shut on the client too");
    }

    [Fact]
    public void DestroyingReopensThePortalThenForgetsIt()
    {
        uint p = _portals.Create(3, 1);
        _portals.SetOpen(p, false);
        _portals.Destroy(p);
        _portals.SetOpen(p, false);

        Walk(Vector.Zero);

        Assert.Equal(new[] { "3=1", "3=0", "3=1" }, _engine.States);
        Assert.Empty(_engine.Batches.Single().Numbers);
    }

    /// <summary>
    /// The case the never-reused token exists for (areaportal_list_test drove it
    /// natively): remove a portal, make another, and the first entity's stale
    /// token must not shut the second entity's hole. A map-cycle bug.
    /// </summary>
    [Fact]
    public void AStaleTokenDoesNotReachTheNextPortal()
    {
        uint first = _portals.Create(3, 1);
        _portals.Destroy(first);
        uint second = _portals.Create(3, 1);
        _portals.SetOpen(first, false);

        Assert.NotEqual(first, second);
        Assert.Equal(new[] { "3=1", "3=1", "3=1" }, _engine.States);
    }

    [Fact]
    public void AnUnknownTokenIsIgnored()
    {
        _portals.SetOpen(0, false);
        _portals.SetOpen(42, false);
        _portals.Destroy(42);
        _portals.SetWindow(42, Vector.Zero, Vector.Zero, 10f);

        Assert.Empty(_engine.States);
    }

    // ---- the walk -----------------------------------------------------------

    [Fact]
    public void AnOpenPortalFacingTheViewerIsOpenAndItsBitIsSet()
    {
        _portals.Create(3, 1);
        _engine.Planes[3] = (new Vector(1, 0, 0), 0f);

        byte[] bits = Walk(new Vector(10, 0, 0));

        Assert.Equal(new[] { 3 }, _engine.Batches[0].Numbers);
        Assert.Equal(new[] { 1 }, _engine.Batches[0].Open);
        Assert.True(Bit(bits, 3));
    }

    [Theory]
    [InlineData(-79.9f, 1)]   // inside the 80-unit padding: still open
    [InlineData(-80f, 0)]     // exactly the padding: DistTo + 80 > 0 is false
    [InlineData(-200f, 0)]
    public void ThePlaneTestIsTheViewersDistancePlusThePadding(float x, int open)
    {
        _portals.Create(3, 1);
        _engine.Planes[3] = (new Vector(1, 0, 0), 0f);

        byte[] bits = Walk(new Vector(x, 0, 0));

        Assert.Equal(open, _engine.Batches[0].Open[0]);
        Assert.True(Bit(bits, 3), "behind the plane is closed on the server, never on the client");
    }

    [Fact]
    public void ThePlanesDistanceIsSubtracted()
    {
        _portals.Create(3, 1);
        _engine.Planes[3] = (new Vector(0, 0, 1), 100f);

        Walk(new Vector(0, 0, 19f));      // 19 - 100 + 80 = -1
        Walk(new Vector(0, 0, 21f));      // 21 - 100 + 80 = 1

        Assert.Equal(new[] { 0, 1 }, _engine.Batches.Select(b => b.Open[0]));
    }

    [Fact]
    public void APortalTheEngineHasNoPlaneForIsLeftOpen()
    {
        _portals.Create(3, 1);

        Walk(new Vector(-1000, 0, 0));

        Assert.Equal(1, _engine.Batches[0].Open[0]);
    }

    [Fact]
    public void AVersionZeroPortalIsAlwaysOpenOnTheClient()
    {
        uint p = _portals.Create(3, 0);
        _portals.SetOpen(p, false);

        byte[] bits = Walk(Vector.Zero);

        Assert.Equal(0, _engine.Batches[0].Open[0]);
        Assert.True(Bit(bits, 3));
    }

    [Fact]
    public void TheWalkIsInCreationOrderAndBatchedAt512()
    {
        for (int i = 0; i < 513; i++)
            _portals.Create(i % 100, 1);

        Walk(Vector.Zero);

        Assert.Equal(new[] { 512, 1 }, _engine.Batches.Select(b => b.Numbers.Length));
        Assert.Equal(Enumerable.Range(0, 512).Select(i => i % 100), _engine.Batches[0].Numbers);
        Assert.Equal(12, _engine.Batches[1].Numbers[0]);
    }

    [Fact]
    public void ExactlyABatchIsFlushedThenFlushedAgainEmpty()
    {
        for (int i = 0; i < 512; i++)
            _portals.Create(1, 1);

        Walk(Vector.Zero);

        Assert.Equal(new[] { 512, 0 }, _engine.Batches.Select(b => b.Numbers.Length));
    }

    [Fact]
    public void APortalNumberPastTheBitsIsTheErrorNativeRaises()
    {
        _portals.Create(2, 1);
        _portals.Create(192, 1);

        var bits = new byte[24];
        Assert.Equal(192, _portals.Visibility(Vector.Zero, 1f, bits));
    }

    // ---- windows ------------------------------------------------------------

    [Fact]
    public void AWindowBeyondItsFadeIsShutOnBothSides()
    {
        uint p = _portals.Create(4, 1);
        _portals.SetWindow(p, new Vector(0, 0, 0), new Vector(10, 10, 10), 100f);

        byte[] near = Walk(new Vector(10 + 109f, 5, 5));   // 109 <= 110
        byte[] far = Walk(new Vector(10 + 111f, 5, 5));    // 111 > 110

        Assert.Equal(new[] { 1, 0 }, _engine.Batches.Select(b => b.Open[0]));
        Assert.True(Bit(near, 4));
        Assert.False(Bit(far, 4));
    }

    [Fact]
    public void TheFovFactorScalesTheWindowsDistance()
    {
        uint p = _portals.Create(4, 1);
        _portals.SetWindow(p, Vector.Zero, new Vector(10, 10, 10), 100f);

        Walk(new Vector(10 + 60f, 5, 5), fov: 2f);   // 120 > 110

        Assert.Equal(0, _engine.Batches[0].Open[0]);
    }

    [Fact]
    public void AWindowWithNoFadeDistanceIsNotAWindow()
    {
        uint p = _portals.Create(4, 1);
        _portals.SetWindow(p, Vector.Zero, new Vector(10, 10, 10), 0f);

        Walk(new Vector(5000, 5, 5));

        Assert.Equal(1, _engine.Batches[0].Open[0]);
    }

    // ---- a hot reload -------------------------------------------------------

    [Fact]
    public void AReloadCarriesEveryRecordAndTheNextTokenAndTellsTheEngineNothing()
    {
        uint a = _portals.Create(3, 1);
        uint b = _portals.Create(4, 0);
        _portals.SetOpen(a, false);
        _portals.SetWindow(b, new Vector(0, 0, 0), new Vector(10, 10, 10), 100f);
        int told = _engine.States.Count;

        var record = new ReloadRecord("SourceSharp.Server.AreaPortals");
        Assert.IsAssignableFrom<ISaveCustom>(_portals).OnSave(new SaveRecordAccess(record, record.Owner, new RecordingLog()));
        var after = new AreaPortals(_engine);
        Assert.IsAssignableFrom<ISaveCustom>(after).OnRestore(new SaveRecordAccess(record, record.Owner, new RecordingLog()));

        Assert.Equal(_portals.Describe(), after.Describe());
        Assert.Equal(2, after.Count);
        Assert.Equal(told, _engine.States.Count);
        Assert.Equal(3u, after.Create(7, 1));
        after.SetOpen(a, true);
        Assert.Equal("3=1", _engine.States[^1]);
    }

    [Fact]
    public void ALevelShutdownForgetsEveryRecord()
    {
        _portals.Create(3, 1);
        ((IGameLifecycle)_portals).OnLevelShutdown();

        Assert.Equal(0, _portals.Count);
    }

#if DEBUG
    /// <summary>
    /// cs_sv_areaportals, which was the native readout over g_AreaPortals: a
    /// count, then one line per record in walk order, in the native words.
    /// </summary>
    [Fact]
    public void TheReadoutIsTheCountThenOneLinePerRecordInWalkOrder()
    {
        uint a = _portals.Create(3, 1);
        uint b = _portals.Create(4, 0);
        _portals.SetOpen(a, false);
        _portals.SetWindow(b, Vector.Zero, new Vector(10, 10, 10), 250f);
        var console = new RecordingEngine();

        new AreaPortalsCommand(_portals, console).Execute("");

        Assert.Equal(new[]
        {
            "[SourceSharp] area portals: 2 in the list\n",
            "[SourceSharp]   element 0: number=3 version=1 open=0 window=0 fade=0 token=1\n",
            "[SourceSharp]   element 1: number=4 version=0 open=1 window=1 fade=250 token=2\n",
        }, console.Calls.Where(c => c.Member == "Console.Msg").Select(c => (string)c.Arg(0)!));
    }
#endif

    [Fact]
    public void TheDistanceToABoxIsZeroInsideAndEuclideanOutside()
    {
        var lo = new Vector(0, 0, 0);
        var hi = new Vector(10, 10, 10);

        Assert.Equal(0f, AreaPortals.DistanceToBox(lo, hi, new Vector(5, 5, 5)));
        Assert.Equal(5f, AreaPortals.DistanceToBox(lo, hi, new Vector(-3, 14, 5)));
        Assert.Equal(MathF.Sqrt(3f), AreaPortals.DistanceToBox(lo, hi, new Vector(11, 11, -1)));
    }
}
