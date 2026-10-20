using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;
using SourceSharp.Server.Interop;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables E16: the server's net-state layout asks, as
/// NetStateLayoutBinding answers them. Marshalling only: what an answer is belongs
/// to StandaloneNetStates (NetStateFieldOffsetTests), so these compare against
/// it and require a real, non-negative offset in the positive arm.
/// </summary>
public unsafe class NetStateLayoutBindingTests
{
    // This class's own catalog of the shared declarations (statics to zero).
    private readonly NetStateCatalog _catalog = NetStateCatalog.Of(TestServices.ServerGame, new RecordingLog());

    private readonly NetStateLayoutBinding _binding;

    public NetStateLayoutBindingTests() => _binding = new(new Crossings(), new RecordingLog(), _catalog);

    private static (string Class, string Field) AKnownField()
    {
        var state = StandaloneNetStates.Discover(typeof(BreakableSurfaceNetState).Assembly, new RecordingLog())
                                       .First(s => s.Fields.Count > 0);
        return (state.ClassName, state.Fields[0].Name);
    }

    private delegate int Ask(byte* pClass, byte* pField);

    private static int Call(Ask ask, string cls, string field)
    {
        var c = Encoding.UTF8.GetBytes(cls + "\0");
        var f = Encoding.UTF8.GetBytes(field + "\0");
        fixed (byte* pc = c)
        fixed (byte* pf = f)
            return ask(pc, pf);
    }

    [Fact]
    public void AKnownFieldsOffsetIsTheDeclarationsAnswer()
    {
        var (cls, field) = AKnownField();
        int expected = _catalog.FieldOffset(cls, field);

        int answered = Call(_binding.NetStateFieldOffset, cls, field);

        Assert.True(expected >= 0 && answered == expected, $"{cls}.{field}: {answered}, declared {expected}");
    }

    [Fact]
    public void AKnownFieldsBitsAreTheDeclarationsAnswer()
    {
        var (cls, field) = AKnownField();
        int expected = _catalog.FieldBits(cls, field);

        int answered = Call(_binding.NetStateFieldBits, cls, field);

        Assert.True(answered == expected, $"{cls}.{field}: {answered}, declared {expected}");
    }

    [Fact]
    public void AnUnpublishedClassAnswersMinusOne()
    {
        int offset = Call(_binding.NetStateFieldOffset, "no_such_netclass", "Health");
        int bits = Call(_binding.NetStateFieldBits, "no_such_netclass", "Health");

        Assert.True(offset == -1 && bits == -1, $"not published: offset {offset}, bits {bits}");
    }
}
