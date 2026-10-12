using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;
using SourceSharp.Server.Interop;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables E13: the server's native temp-entity forward, as
/// TempEntsBinding answers it. Native sends what comes back, so every refusal
/// must be -1 (send nothing), never 0.
/// </summary>
public unsafe class TempEntsBindingTests
{
    private sealed class FakeBlobs : ITempEntBlobs
    {
        public int Calls;
        public int Id;
        public float[] Floats = [];
        public int[] Ints = [];
        public Func<int>? Answer;
        public bool Throw;

        public int BuildForwardBlob(int id, ReadOnlySpan<float> floats, ReadOnlySpan<int> ints,
                                    Span<byte> into, out int classHandle)
        {
            Calls++;
            Id = id;
            Floats = floats.ToArray();
            Ints = ints.ToArray();
            classHandle = 7;
            if (Throw) throw new InvalidOperationException("thrown on purpose");
            into[0] = 0xAB;
            into[1] = 0xCD;
            return Answer?.Invoke() ?? 2;
        }
    }

    private readonly FakeBlobs _blobs = new();

    private int Forward(bool withBlob, byte[] blob, int* pClass)
    {
        var binding = new TempEntsBinding(new Crossings(), new RecordingLog(), _blobs);
        float[] floats = [1.5f, 2.5f];
        int[] ints = [9];
        fixed (float* f = floats)
        fixed (int* i = ints)
        fixed (byte* b = blob)
            return binding.OnNativeTempEntity(42, f, floats.Length, i, ints.Length,
                                              withBlob ? b : null, blob.Length, pClass);
    }

    [Fact]
    public void AForwardWritesItsBlobAndAnswersItsLength()
    {
        var blob = new byte[16];
        int hClass = -1;

        int bytes = Forward(true, blob, &hClass);

        Assert.True(bytes == 2 && blob[0] == 0xAB && blob[1] == 0xCD,
                    $"the module's two bytes come back ({bytes}, {blob[0]:X2}{blob[1]:X2})");
    }

    [Fact]
    public void AForwardSaysWhichClassToSendAs()
    {
        int hClass = -1;
        Forward(true, new byte[16], &hClass);

        Assert.True(hClass == 7, $"the class handle is written ({hClass})");
    }

    [Fact]
    public void TheArgumentsReachTheModule()
    {
        int hClass = -1;
        Forward(true, new byte[16], &hClass);

        Assert.True(_blobs.Calls == 1 && _blobs.Id == 42
                    && _blobs.Floats.SequenceEqual([1.5f, 2.5f]) && _blobs.Ints.SequenceEqual([9]),
                    $"id, floats and ints arrive ({_blobs.Calls} call(s), id {_blobs.Id})");
    }

    [Fact]
    public void NoBlobBufferAnswersMinusOneWithoutAsking()
    {
        int hClass = -1;

        int bytes = Forward(false, new byte[16], &hClass);

        Assert.True(bytes == -1 && _blobs.Calls == 0, $"no buffer, no ask ({bytes}, {_blobs.Calls} call(s))");
    }

    [Fact]
    public void ARefusalLeavesTheClassUnwritten()
    {
        _blobs.Answer = () => -1;
        int hClass = -1;

        int bytes = Forward(true, new byte[16], &hClass);

        Assert.True(bytes == -1 && hClass == -1, $"a refusal sends nothing ({bytes}, class {hClass})");
    }

    [Fact]
    public void AThrowingModuleAnswersMinusOneNotZero()
    {
        _blobs.Throw = true;
        int hClass = -1;

        int bytes = Forward(true, new byte[16], &hClass);

        Assert.True(bytes == -1 && _blobs.Calls == 1, $"a fault sends nothing ({bytes})");
    }
}
