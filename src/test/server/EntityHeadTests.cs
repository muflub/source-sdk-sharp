using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// An entity's solidity and world-space centre are read from its engine
/// block's head, which is what its collideable answers (plan_server_native_v2
/// T4). The world has no block: it is SOLID_BSP with no flags, at the origin
/// with no bounds. A handle that resolves to nothing answers zero. The recorders are static (an unmanaged callback cannot close
/// over an instance) and only this class's facts touch them.
/// </summary>
public sealed unsafe class EntityHeadTests : System.IDisposable
{
    private static byte* s_Block;
    private static int s_Index;

    [UnmanagedCallersOnly]
    private static void* GetBlock(void* self, uint h, int* size)
    {
        *size = s_Block == null ? 0 : EngineBlock.SizeBytes;
        return s_Block;
    }

    [UnmanagedCallersOnly]
    private static int GetIndex(void* self, uint h) => s_Index;

    private readonly FakeShimRoot _root = new();
    private readonly EngineSide _side = TestSides.Create(Side.Server);
    private readonly byte* _block = (byte*)NativeMemory.AllocZeroed((nuint)EngineBlock.SizeBytes);

    public EntityHeadTests()
    {
        s_Block = null;
        s_Index = -1;
        _root.Live("SourceSharpNetworkServer001", "EntityGetEngineBlock", (nint)(delegate* unmanaged<void*, uint, int*, void*>)&GetBlock)
             .Live("SourceSharpEntitiesServer001", "EntityGetIndex", (nint)(delegate* unmanaged<void*, uint, int>)&GetIndex);
        _side.Attachment.Initialize();
        Assert.True(_side.Attachment.AttachShims(_root.Bind(), out string why), why);
    }

    public void Dispose()
    {
        _root.Dispose();
        s_Block = null;
        NativeMemory.Free(_block);
    }

    private IEngineEntities Entities => SideBindings.Of<IEngineEntities>(_side);

    private static readonly EntityHandle Ent = new(0x00120034);

    // Entry 0 named on purpose: the world.
    private static readonly EntityHandle World = new(0x00000000);

    [Fact]
    public void AnEntitysShapeAndFlagsAreItsBlockHeads()
    {
        *(int*)(_block + EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.SolidType))) = (int)SolidType.VPhysics;
        *(int*)(_block + EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.SolidFlags))) = (int)(SolidFlags.Trigger | SolidFlags.NotSolid);
        s_Block = _block;
        s_Index = 7;

        Assert.Equal(SolidType.VPhysics, Entities.GetSolidType(Ent));
        Assert.Equal(SolidFlags.Trigger | SolidFlags.NotSolid, Entities.GetSolidFlags(Ent));
    }

    [Fact]
    public void TheWorldHasNoBlockAndIsBspWithNoFlags()
    {
        s_Index = 0;

        Assert.Equal(SolidType.Bsp, Entities.GetSolidType(World));
        Assert.Equal(SolidFlags.None, Entities.GetSolidFlags(World));
    }

    [Fact]
    public void AHandleThatResolvesToNothingAnswersZero()
    {
        Assert.Equal(SolidType.None, Entities.GetSolidType(Ent));
        Assert.Equal(SolidFlags.None, Entities.GetSolidFlags(Ent));
        Assert.Equal(default, Entities.GetWorldSpaceCenter(Ent));
    }

    [Fact]
    public void TheCentreIsTheOriginPlusTheMiddleOfTheBox()
    {
        *(Vector*)(_block + EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Origin))) = new Vector(100f, 200f, 300f);
        *(Vector*)(_block + EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Mins))) = new Vector(-16f, -8f, 0f);
        *(Vector*)(_block + EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Maxs))) = new Vector(32f, 8f, 72f);
        s_Block = _block;

        Assert.Equal(new Vector(108f, 200f, 336f), Entities.GetWorldSpaceCenter(Ent));
    }

    [Fact]
    public void TheWorldsCentreIsTheOrigin()
    {
        s_Index = 0;

        Assert.Equal(default, Entities.GetWorldSpaceCenter(World));
    }
}
