using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A static that held a closure over the server module pinned the server's
/// load context, and a hot reload then leaked it (the unload report printed
/// "PIN SourceSharp.Abi.PlayerCommandQueue.MaxMoveMagnitude" and
/// "PIN ... PlayerShadow.AdvanceSwitch"). Statics to zero removed the slots:
/// the queue, its magnitude reader and its shadow are each server
/// composition's own, so there is nothing for a shutdown to take back. (The
/// switch itself went with the Advance that physics parity B7 replaced.)
/// </summary>
public class ServerModuleReloadPinTests
{
    private static IEnumerable<FieldInfo> ObjectSlots(Type type)
        => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
               .Where(f => !f.IsLiteral && !f.FieldType.IsValueType);

    [Fact]
    public void NeitherTheQueueNorTheShadowHasAStaticAModuleCouldPin()
    {
        // The reach check: the scan reads static fields, and finds them on a
        // type that has one.
        Assert.NotEmpty(ObjectSlots(typeof(StaticStateWaivers)));

        Assert.Empty(ObjectSlots(typeof(PlayerCommandQueue)));
        Assert.Empty(ObjectSlots(typeof(PlayerShadow)));
    }

    /// <summary>
    /// Each server composition's queue is its own: what one module sets as its
    /// magnitude reader is not another's, and neither is its shadow.
    /// </summary>
    [Fact]
    public void TwoQueuesHoldTheirOwnReaderAndShadow()
    {
        var mine = new PlayerCommandQueue(new RecordingLog());
        var other = new PlayerCommandQueue(new RecordingLog());

        mine.MaxMoveMagnitude = () => 1f;

        Assert.NotNull(mine.MaxMoveMagnitude);   // the stimulus landed
        Assert.Null(other.MaxMoveMagnitude);
        Assert.NotSame(mine.Shadow, other.Shadow);
    }
}
