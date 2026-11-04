using System.Runtime.InteropServices;

using SourceSharp;
using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The flame's points cross as records of ten floats. The native side walks a
/// count of records, so a count of floats reads past the live points into the
/// ring's empty tail, whose zeros set control point 0 to the world's origin:
/// every particle then burned there, out of sight.
/// </summary>
public unsafe class FlamePointsFeedTests
{
    private static int s_Count;

    [UnmanagedCallersOnly]
    private static int RecordsCount(void* self, uint owner, int slot, int active, void* values, int count)
    {
        s_Count = count;
        return 1;
    }

    [Fact]
    public void The_feed_crosses_its_count_of_records()
    {
        var self = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));
        var table = (ShimEffectsRenderClientFwd*)NativeMemory.AllocZeroed(1, (nuint)sizeof(ShimEffectsRenderClientFwd));
        try
        {
            table->ParticleSystemSetPoints = &RecordsCount;
            using var root = new FakeShimRoot().Area("SourceSharpEffectsRenderClient001", self, table);
            var side = TestSides.Create(Side.Client);
            side.Attachment.Initialize();
            Assert.True(side.Attachment.AttachShims(root.Bind(), out string why), why);
            var effects = SideBindings.Of<IEngineEffects>(side);
            s_Count = -1;

            effects.ParticleSystemSetPoints(EntityHandle.None, 0, 1, new float[20]);

            Assert.Equal(2, s_Count);
        }
        finally
        {
            NativeMemory.Free(table);
            NativeMemory.Free(self);
        }
    }
}
