using System.Collections.Generic;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ServerEntityPlacement"/>: the lag compensation's move is the
/// transform area's teleport with no velocity change, and nothing else the
/// managed teleport adds.
/// </summary>
public unsafe class ServerEntityPlacementTests
{
    private static readonly EntityHandle Entity = new(0x0001_0004u);

    private sealed class Transform : ITransformArea
    {
        public readonly List<(uint Entity, Vector Origin, QAngle Angles, int ZeroVelocity)> Teleports = [];
        public int OtherCalls;

        public void EntityTeleport(uint a0, float* a1, float* a2, int a3)
            => Teleports.Add((a0, new Vector(a1[0], a1[1], a1[2]), new QAngle(a2[0], a2[1], a2[2]), a3));

        public void EntitySetOrigin(uint a0, float* a1) => OtherCalls++;
        public void EntitySetAngles(uint a0, float* a1) => OtherCalls++;
        public void EntityGetVelocity(uint a0, float* a1) => OtherCalls++;
        public void EntitySetVelocity(uint a0, float* a1) => OtherCalls++;
        public void EntityGetAngularVelocity(uint a0, float* a1) => OtherCalls++;
        public void EntitySetAngularVelocity(uint a0, float* a1) => OtherCalls++;
        public void EntityGetEyePosition(uint a0, float* a1) => OtherCalls++;
        public void EntityGetEyeAngles(uint a0, float* a1) => OtherCalls++;
        public float EntityGetGravity(uint a0) { OtherCalls++; return 0f; }
        public void EntitySetGravity(uint a0, float a1) => OtherCalls++;
        public void EntitySetBounce(uint a0, int a1) => OtherCalls++;
    }

    private static (ServerEntityPlacement Placement, Transform Transform) Placement()
    {
        var transform = new Transform();
        return (new ServerEntityPlacement(RecordingEngine.Default(), transform), transform);
    }

    [Fact]
    public void ARelinkIsOneTeleportThatKeepsTheVelocity()
    {
        var (placement, transform) = Placement();

        placement.Relink(Entity, new Vector(1f, 2f, 3f), new QAngle(0f, 90f, 0f));

        Assert.Equal([(Entity.Value, new Vector(1f, 2f, 3f), new QAngle(0f, 90f, 0f), 0)], transform.Teleports);
        Assert.Equal(0, transform.OtherCalls);
    }

    [Fact]
    public void TheReadIsTheBlocksOriginAnglesAndBox()
    {
        EngineBlock block = default;
        block.Head.Origin = new Vector(1f, 2f, 3f);
        block.Head.Angles = new QAngle(4f, 5f, 6f);
        block.Head.Mins = new Vector(-16f, -16f, 0f);
        block.Head.Maxs = new Vector(16f, 16f, 72f);
        byte[] bytes = new byte[EngineBlock.SizeBytes];
        System.Runtime.InteropServices.MemoryMarshal.Write(bytes, in block);
        var engine = RecordingEngine.Default().Returns("Entities.GetEngineBlock", bytes);

        var placement = new ServerEntityPlacement(engine, new Transform());

        Assert.True(placement.TryRead(Entity, out EntityPlacement read));
        Assert.Equal(new EntityPlacement(new Vector(1f, 2f, 3f), new QAngle(4f, 5f, 6f),
                                         new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)), read);
    }

    [Fact]
    public void AnEntityWithNoBlockReadsNothing()
    {
        var (placement, _) = Placement();

        Assert.False(placement.TryRead(Entity, out EntityPlacement read));
        Assert.Equal(default, read);
    }
}
