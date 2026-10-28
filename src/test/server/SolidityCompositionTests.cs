using System.Collections.Generic;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_native_v2 T4d: four collision slots that were compositions of
/// slots the shim already had (EntityMakePickup, EntitySetPickupEnabled,
/// EntitySetBrushSolidity, EntitySetNotSolid) are made in managed code. Each
/// fact is the native body's own sequence, read off shell_entityapi.cpp before
/// the move: the same calls, in the same order, with the same values, through
/// EntitySetBounds, EntitySetVisible and EntitySetSolidity, with the entity's
/// solidity read from its engine block's head.
///
/// <para>
/// Recorded through a fake root, so a fact sees exactly what crossed. The
/// collision values are const.h's: SOLID_NONE 0, SOLID_BSP 1, SOLID_BBOX 2;
/// FSOLID_NOT_SOLID 0x4, FSOLID_TRIGGER 0x8. The recorders are static (an
/// unmanaged callback cannot close over an instance) and only this class's
/// facts, which xUnit runs one at a time, touch them.
/// </para>
/// </summary>
public sealed unsafe class SolidityCompositionTests : System.IDisposable
{
    private const string Entities = "SourceSharpEntitiesServer001";
    private const string Collision = "SourceSharpCollisionServer001";
    private const string Network = "SourceSharpNetworkServer001";

    private static readonly List<string> s_Calls = new();
    private static byte* s_Block;

    [UnmanagedCallersOnly]
    private static void SetBounds(void* self, uint h, void* mins, void* maxs)
    {
        var lo = (float*)mins;
        var hi = (float*)maxs;
        s_Calls.Add($"SetBounds {h} ({lo[0]} {lo[1]} {lo[2]}) ({hi[0]} {hi[1]} {hi[2]})");
    }

    [UnmanagedCallersOnly]
    private static void SetSolidity(void* self, uint h, int type, int flags)
        => s_Calls.Add($"SetSolidity {h} {type} 0x{flags:x}");

    [UnmanagedCallersOnly]
    private static void SetVisible(void* self, uint h, int visible)
        => s_Calls.Add($"SetVisible {h} {visible}");

    [UnmanagedCallersOnly]
    private static void* GetBlock(void* self, uint h, int* size)
    {
        *size = EngineBlock.SizeBytes;
        return s_Block;
    }

    private static void SetHead(int solidType, int solidFlags)
    {
        *(int*)(s_Block + EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.SolidType))) = solidType;
        *(int*)(s_Block + EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.SolidFlags))) = solidFlags;
    }

    private readonly FakeShimRoot _root = new();
    private readonly EngineSide _side = TestSides.Create(Side.Server);

    public SolidityCompositionTests()
    {
        s_Calls.Clear();
        s_Block = (byte*)NativeMemory.AllocZeroed((nuint)EngineBlock.SizeBytes);
        _root.Live(Collision, "EntitySetBounds", (nint)(delegate* unmanaged<void*, uint, void*, void*, void>)&SetBounds)
             .Live(Collision, "EntitySetSolidity", (nint)(delegate* unmanaged<void*, uint, int, int, void>)&SetSolidity)
             .Live(Network, "EntityGetEngineBlock", (nint)(delegate* unmanaged<void*, uint, int*, void*>)&GetBlock)
             .Live(Entities, "EntitySetVisible", (nint)(delegate* unmanaged<void*, uint, int, void>)&SetVisible);
        _side.Attachment.Initialize();
        Assert.True(_side.Attachment.AttachShims(_root.Bind(), out string why), why);
    }

    public void Dispose()
    {
        _root.Dispose();
        NativeMemory.Free(s_Block);
        s_Block = null;
    }

    private IEnginePlayers Players => SideBindings.Of<IEnginePlayers>(_side);

    private IEngineEntityPhysics Physics => SideBindings.Of<IEngineEntityPhysics>(_side);

    private static readonly EntityHandle Ent = new(0x00120034);

    [Fact]
    public void APickupIsItsBoxOnTheFloorThenATriggerThatIsNotSolid()
    {
        Players.MakePickup(Ent, 16f, 8f, 24f);

        Assert.Equal(new[]
        {
            "SetBounds 1179700 (-16 -8 0) (16 8 24)",
            "SetSolidity 1179700 2 0xc",
        }, s_Calls);
    }

    [Fact]
    public void AnEnabledPickupIsShownThenMadeATriggerBox()
    {
        Players.SetPickupEnabled(Ent, true);

        Assert.Equal(new[] { "SetVisible 1179700 1", "SetSolidity 1179700 2 0xc" }, s_Calls);
    }

    [Fact]
    public void ADisabledPickupIsHiddenThenLeavesTheTriggerListEntirely()
    {
        Players.SetPickupEnabled(Ent, false);

        Assert.Equal(new[] { "SetVisible 1179700 0", "SetSolidity 1179700 0 0x4" }, s_Calls);
    }

    [Theory]
    [InlineData(BrushSolidity.None, "SetSolidity 1179700 1 0x4")]
    [InlineData(BrushSolidity.Trigger, "SetSolidity 1179700 1 0xc")]
    [InlineData(BrushSolidity.Solid, "SetSolidity 1179700 1 0x0")]
    [InlineData((BrushSolidity)7, "SetSolidity 1179700 1 0x0")]
    public void ABrushStaysBspAndOnlyItsFlagsChange(BrushSolidity solidity, string expected)
    {
        Physics.SetBrushSolidity(Ent, solidity);

        Assert.Equal(new[] { expected }, s_Calls);
    }

    [Fact]
    public void NotSolidAddsTheOneFlagAndKeepsTheShapeAndTheOtherFlags()
    {
        SetHead(2, 0x8 | 0x20);

        Physics.SetNotSolid(Ent, true);

        Assert.Equal(new[] { "SetSolidity 1179700 2 0x2c" }, s_Calls);
    }

    [Fact]
    public void SolidAgainClearsOnlyThatFlag()
    {
        SetHead(1, 0x4 | 0x8);

        Physics.SetNotSolid(Ent, false);

        Assert.Equal(new[] { "SetSolidity 1179700 1 0x8" }, s_Calls);
    }
}
