using SourceSharp.Abi;
using SourceSharp.Client.Physics;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's collision solver against stock's client CCollisionEvent
/// (game/client/physics.cpp): ShouldCollide's arms for managed-owned objects,
/// and the four slots stock answers with a constant (lane c6-input, C6
/// cluster 9).
/// </summary>
public class ClientCollisionSolverTests
{
    private const ushort PartOfRagdoll = 0x0008, NoSelfCollisions = 0x8000;

    private sealed class Facts : IClientCollisionFacts
    {
        public readonly Dictionary<nint, ushort> Flags = new();
        public readonly Dictionary<EntityHandle, EngineCollisionGroup> Groups = new();
        public ushort GameFlags(nint o) => Flags.TryGetValue(o, out ushort f) ? f : (ushort)0;
        public EngineCollisionGroup GroupOf(EntityHandle h) => Groups.TryGetValue(h, out var g) ? g : EngineCollisionGroup.None;
        public readonly Dictionary<nint, uint> ContentsOf = new();
        public readonly HashSet<nint> Shadowed = new();
        public uint Contents(nint o) => ContentsOf.TryGetValue(o, out uint c) ? c : 1u;   // CONTENTS_SOLID
        public bool HasShadowController(nint o) => Shadowed.Contains(o);
        public bool RagdollCollide { get; set; }
    }

    private static readonly EntityHandle A = new(0x0002_0011u), B = new(0x0003_0012u);

    private static nint Word(EntityHandle h) => unchecked((nint)(PhysicsGameData.Tag | (h.Value + PhysicsGameData.Bias)));

    private static (ClientCollisionSolver Solver, Facts Facts) Rig()
    {
        var f = new Facts();
        f.Groups[A] = EngineCollisionGroup.Debris;
        f.Groups[B] = EngineCollisionGroup.Debris;
        f.Flags[1] = PartOfRagdoll;
        f.Flags[2] = PartOfRagdoll;
        return (new ClientCollisionSolver(f), f);
    }

    [Fact]
    public void AnObjectWithNoOwnerCollides()
    {
        var (s, _) = Rig();
        Assert.Equal(1, s.ShouldCollide(1, 2, Word(A), 0));
        Assert.Equal(1, s.ShouldCollide(1, 2, 0, Word(B)));
    }

    [Fact]
    public void TwoPiecesOfOneRagdollCollide()
    {
        var (s, _) = Rig();
        Assert.Equal(1, s.ShouldCollide(1, 2, Word(A), Word(A)));
    }

    [Fact]
    public void NoSelfCollisionsOnEitherPieceStopsTheSameOwner()
    {
        var (s, f) = Rig();
        f.Flags[2] = PartOfRagdoll | NoSelfCollisions;
        Assert.Equal(0, s.ShouldCollide(1, 2, Word(A), Word(A)));
    }

    [Fact]
    public void TwoRagdollsAsDebrisPassThroughEachOther()
    {
        var (s, _) = Rig();
        Assert.Equal(0, s.ShouldCollide(1, 2, Word(A), Word(B)));
    }

    [Fact]
    public void TwoRagdollPartsOfCollidingGroupsObeyClRagdollCollide()
    {
        var (s, f) = Rig();
        f.Groups[A] = EngineCollisionGroup.None;
        f.Groups[B] = EngineCollisionGroup.None;
        Assert.Equal(0, s.ShouldCollide(1, 2, Word(A), Word(B)));
        f.RagdollCollide = true;
        Assert.Equal(1, s.ShouldCollide(1, 2, Word(A), Word(B)));
    }

    [Fact]
    public void CollidingGroupsWithAPieceThatIsNoRagdollCollide()
    {
        var (s, f) = Rig();
        f.Groups[A] = EngineCollisionGroup.None;
        f.Groups[B] = EngineCollisionGroup.None;
        f.Flags[2] = 0;
        Assert.Equal(1, s.ShouldCollide(1, 2, Word(A), Word(B)));
    }

    [Fact]
    public void AnObjectWithNoSolidContentsDoesNotCollide()
    {
        var (s, f) = Rig();
        f.Groups[A] = EngineCollisionGroup.None;
        f.Groups[B] = EngineCollisionGroup.None;
        f.Flags[2] = 0;
        f.ContentsOf[2] = 0x20;   // CONTENTS_WATER
        Assert.Equal(0, s.ShouldCollide(1, 2, Word(A), Word(B)));
    }

    [Fact]
    public void TwoShadowControlledObjectsDoNotCollide()
    {
        var (s, f) = Rig();
        f.Groups[A] = EngineCollisionGroup.None;
        f.Groups[B] = EngineCollisionGroup.None;
        f.Flags[2] = 0;
        f.Shadowed.Add(1);
        Assert.Equal(1, s.ShouldCollide(1, 2, Word(A), Word(B)));
        f.Shadowed.Add(2);
        Assert.Equal(0, s.ShouldCollide(1, 2, Word(A), Word(B)));
    }

    [Fact]
    public void ItCountsThePairsAskedAndRefused()
    {
        var (s, _) = Rig();
        s.ShouldCollide(1, 2, Word(A), Word(B));
        s.ShouldCollide(1, 2, Word(A), 0);
        Assert.Equal(2, s.Asked);
        Assert.Equal(1, s.Refused);
    }

    [Fact]
    public void TheConstantSlotsAnswerAsStock()
    {
        var (s, _) = Rig();
        Assert.Equal(1, s.ShouldSolvePenetration(1, 2, Word(A), Word(B), 0.015f));
        Assert.True(s.ShouldFreezeObject(1));
        Assert.Equal(0, s.AdditionalCollisionChecksThisTick(3));
        Assert.True(s.ShouldFreezeContacts(4));
    }
}
