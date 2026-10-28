using SourceSharp.Abi;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_client_vtables CE6: TempEntsClientBinding's answers. The generated
/// thunk's catch answers 0, and for three slots 0 is not a refusal (a block
/// of 0 is a block native would act on), so the
/// binding keeps the deleted Entry.cs thunks' -1 itself. Each fault fact also
/// requires that the module was reached, so a binding that refused before
/// asking could not pass it. The pool's alloc and eject-brass slots are
/// retired — their bodies answer -1/0 and must never reach the module, which
/// is the same fact in the other direction.
/// </summary>
public unsafe class TempEntsClientBindingTests
{
    private sealed class FakeClient : IClientModule
    {
        public readonly List<string> Calls = [];
        public bool Throw;
        public bool BoolAnswer;

        private void Reached(string name)
        {
            Calls.Add(name);
            if (Throw) throw new InvalidOperationException("thrown on purpose");
        }

        public void DispatchTempEntity(int handle, ReadOnlySpan<byte> blob) => Reached(nameof(DispatchTempEntity));
        public bool ForwardTempEntity(int id, ReadOnlySpan<float> floats, ReadOnlySpan<int> ints)
        { Reached(nameof(ForwardTempEntity)); return BoolAnswer; }
        public void TempEntPoolUpdate(float frameTime) => Reached(nameof(TempEntPoolUpdate));
        public void TempEntPoolClear() => Reached(nameof(TempEntPoolClear));
        public int ImpactChoice(ReadOnlySpan<int> input, Span<int> output, Span<byte> particleName, Span<byte> decalName)
        { Reached(nameof(ImpactChoice)); return 0; }
        public (bool CanPredict, int Recipients) SuppressAsked;
        public bool SuppressLocalTempEnt(bool canPredict, int recipientCount)
        { Reached(nameof(SuppressLocalTempEnt)); SuppressAsked = (canPredict, recipientCount); return BoolAnswer; }
        public bool SchemaAccept(uint typeId, bool published, uint expected, uint seen, int entIndex, string className) => throw new NotSupportedException();

        // Not this binding's: a call here is a wiring fault, never a default answer.
        public void DispatchUserMessage(int index, ReadOnlySpan<byte> payload) => throw new NotSupportedException();
        public float GetJumpScale(in JumpRequest request) => throw new NotSupportedException();
        public float GetFrictionScale(int playerIndex, float friction, float speed, bool ducking, int groundMaterial) => throw new NotSupportedException();
        public float GetMaxSpeedScale(int playerIndex, float maxSpeed, bool ducking, int groundMaterial) => throw new NotSupportedException();
        public bool StandaloneCreate(EntityHandle entity, string className) => throw new NotSupportedException();
        public void StandaloneSpawn(EntityHandle entity) => throw new NotSupportedException();
        public int StandaloneDraw(EntityHandle entity) => throw new NotSupportedException();
        public int StandaloneDrawExtras(EntityHandle entity, int flags) => throw new NotSupportedException();
        public void StandaloneDrawBatch(ReadOnlySpan<uint> handles, Span<int> results) => throw new NotSupportedException();
        public void StandaloneRemoved(EntityHandle entity) => throw new NotSupportedException();
        public void StandaloneDataChanged(EntityHandle entity, ReadOnlySpan<byte> blob, int updateType) => throw new NotSupportedException();
        public void OnStage(ClientFrameStage stage) => throw new NotSupportedException();
        public void InvalidateMdlCache() => throw new NotSupportedException();
    }

    private readonly FakeClient _client = new();
    private TempEntsClientBinding Binding => new(new Crossings(), new RecordingLog(), _client);

    [Fact]
    public void ALivePoolUpdateStillReachesTheModule()
    {
        // The positive arm for the refusal above: the same walk's live slot.
        Binding.OnTempEntPoolUpdate(0.016f);
        Assert.Equal(["TempEntPoolUpdate"], _client.Calls);
    }

    [Fact]
    public void ANativeForwardThatThrowsAnswersMinusOne()
    {
        _client.Throw = true;
        Assert.Equal(-1, Binding.OnNativeTempEntity(5, null, 0, null, 0, null, 0, null));
        Assert.Equal(["ForwardTempEntity"], _client.Calls);
    }

    [Fact]
    public void ANativeForwardWithABlobBufferIsTheServersAndAnswersMinusOne()
    {
        _client.BoolAnswer = true;
        byte* blob = stackalloc byte[4];
        Assert.Equal(-1, Binding.OnNativeTempEntity(5, null, 0, null, 0, blob, 4, null));
        Assert.Empty(_client.Calls);
    }

    [Fact]
    public void ALocalForwardThatPlayedAnswersZero()
    {
        _client.BoolAnswer = true;
        Assert.Equal(0, Binding.OnNativeTempEntity(5, null, 0, null, 0, null, 0, null));
    }

    [Fact]
    public void AnImpactChoiceThatThrowsAnswersMinusOne()
    {
        _client.Throw = true;
        int* pIn = stackalloc int[2];
        int* pOut = stackalloc int[3];
        Assert.Equal(-1, Binding.OnImpactChoice(pIn, 2, pOut, 3, null, 0, null, 0));
        Assert.Equal(["ImpactChoice"], _client.Calls);
    }

    [Fact]
    public void ALivePoolClearStillReachesTheModule()
    {
        Binding.OnTempEntPoolClear();
        Assert.Equal(["TempEntPoolClear"], _client.Calls);
    }

    [Fact]
    public void TheSuppressionQuestionReachesTheModuleWithBothFacts()
    {
        _client.BoolAnswer = true;
        Assert.Equal(1, Binding.SuppressLocalTempEnt(0, 3));
        Assert.Equal(["SuppressLocalTempEnt"], _client.Calls);
        Assert.Equal((false, 3), _client.SuppressAsked);
    }

    [Fact]
    public void AnEffectTheModuleLetsThroughAnswersZero()
    {
        _client.BoolAnswer = false;
        Assert.Equal(0, Binding.SuppressLocalTempEnt(1, 1));
        Assert.Equal((true, 1), _client.SuppressAsked);
    }
 }
