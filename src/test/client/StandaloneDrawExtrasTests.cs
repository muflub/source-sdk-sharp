using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The one native-&gt;managed draw an entity's extras ride
/// (IManagedStandaloneClient::OnStandaloneDrawExtras): after the local
/// viewmodel's model (stock's ViewModelDrawn) and in a two-pass entity's
/// translucent pass. One crossing per call, the handle and the pass's flags
/// handed through, the module's answer handed back.
/// </summary>
public unsafe class StandaloneDrawExtrasTests
{
    private sealed class ExtrasClient : IClientModule
    {
        public readonly List<(uint Entity, int Flags)> Seen = [];
        public int Answer = -1;

        public int StandaloneDrawExtras(EntityHandle entity, int flags)
        {
            Seen.Add((entity.Value, flags));
            return Answer;
        }

        // Not this slot's: a call here is a wiring fault, never a default answer.
        public void StandaloneDrawBatch(ReadOnlySpan<uint> handles, Span<int> results) => throw new NotSupportedException();
        public void DispatchTempEntity(int handle, ReadOnlySpan<byte> blob) => throw new NotSupportedException();
        public void TempEntPoolUpdate(float frameTime) => throw new NotSupportedException();
        public bool SuppressLocalTempEnt(bool canPredict, int recipientCount) => throw new NotSupportedException();
        public bool SchemaAccept(uint typeId, bool published, uint expected, uint seen, int entIndex, string className) => throw new NotSupportedException();
        public void TempEntPoolClear() => throw new NotSupportedException();
        public int ImpactEffectParams(int effect, int material, float scale, bool noFlecks, Span<float> block) => throw new NotSupportedException();
        public int ImpactChoice(ReadOnlySpan<int> input, Span<int> output, Span<byte> particleName, Span<byte> decalName) => throw new NotSupportedException();
        public void DispatchUserMessage(int index, ReadOnlySpan<byte> payload) => throw new NotSupportedException();
        public float GetJumpScale(in JumpRequest request) => throw new NotSupportedException();
        public float GetFrictionScale(int playerIndex, float friction, float speed, bool ducking, int groundMaterial) => throw new NotSupportedException();
        public float GetMaxSpeedScale(int playerIndex, float maxSpeed, bool ducking, int groundMaterial) => throw new NotSupportedException();
        public bool StandaloneCreate(EntityHandle entity, string className) => throw new NotSupportedException();
        public void StandaloneSpawn(EntityHandle entity) => throw new NotSupportedException();
        public int StandaloneDraw(EntityHandle entity) => throw new NotSupportedException();
        public void StandaloneRemoved(EntityHandle entity) => throw new NotSupportedException();
        public void StandaloneDataChanged(EntityHandle entity, ReadOnlySpan<byte> blob, int updateType) => throw new NotSupportedException();
        public void OnStage(ClientFrameStage stage) => throw new NotSupportedException();
        public void InvalidateMdlCache() => throw new NotSupportedException();
    }

    private readonly Crossings _crossings = new();
    private readonly ExtrasClient _client = new();
    private readonly StandaloneClientBinding _binding;

    public StandaloneDrawExtrasTests()
        => _binding = new(_crossings, new RecordingLog(), _client, new StandaloneMirror(), NetStateCatalog.Of([]));

    [Fact]
    public void TheHandleAndThePassFlagsReachTheModule()
    {
        _binding.OnStandaloneDrawExtras(0x00030009u, unchecked((int)0x80000009));

        Assert.Equal([(0x00030009u, unchecked((int)0x80000009))], _client.Seen);
    }

    [Fact]
    public void TheModulesAnswerIsWhatDrawModelReturns()
    {
        _client.Answer = 1;

        Assert.Equal(1, _binding.OnStandaloneDrawExtras(5u, 1));
    }

    [Fact]
    public void EachCallIsOneCrossing()
    {
        long before = _crossings.Read(CrossingOp.ClientStandaloneDrawExtras);

        _binding.OnStandaloneDrawExtras(5u, 1);
        _binding.OnStandaloneDrawExtras(5u, 1);

        Assert.Equal(before + 2, _crossings.Read(CrossingOp.ClientStandaloneDrawExtras));
    }

    private sealed class Plain(IEngineEntityRender render) : ClientStandaloneEntity(render)
    {
        public override string ClassName => "plain";
    }

    [Fact]
    public void AnEntityWithNoExtrasLetsItsModelDraw()
        => Assert.Equal(-1, new Plain(TestServices.Fake()).OnDrawExtras(1));
}
