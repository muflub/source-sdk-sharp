using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A view's custom draws in one crossing (plan_client_novalve C7 item 7):
/// IManagedStandaloneClient::OnStandaloneDrawBatch counts one crossing for the
/// whole view whatever its size, hands the module native's handles, and answers
/// -1 ("draw in place, crossing per entity") for every entity the module leaves
/// unprepared, whatever was in native's result slots before.
/// </summary>
public unsafe class StandaloneDrawBatchTests
{
    private sealed class BatchClient : IClientModule
    {
        public readonly List<uint> Seen = [];
        public int Batches;

        // Prepares even handles, leaves odd ones alone.
        public void StandaloneDrawBatch(ReadOnlySpan<uint> handles, Span<int> results)
        {
            Batches++;
            for (int i = 0; i < handles.Length; i++)
            {
                Seen.Add(handles[i]);
                if (handles[i] % 2 == 0)
                    results[i] = 1;
            }
        }

        // Not this slot's: a call here is a wiring fault, never a default answer.
        public void DispatchTempEntity(int handle, ReadOnlySpan<byte> blob) => throw new NotSupportedException();
        public void TempEntPoolUpdate(float frameTime) => throw new NotSupportedException();
        public void TempEntPoolClear() => throw new NotSupportedException();
        public int ImpactEffectParams(int effect, int material, float scale, bool noFlecks, Span<float> block) => throw new NotSupportedException();
        public int ImpactChoice(ReadOnlySpan<int> input, Span<int> output, Span<byte> particleName, Span<byte> decalName) => throw new NotSupportedException();
        public bool SuppressLocalTempEnt(bool canPredict, int recipientCount) => throw new NotSupportedException();
        public bool SchemaAccept(uint typeId, bool published, uint expected, uint seen, int entIndex, string className) => throw new NotSupportedException();
        public void DispatchUserMessage(int index, ReadOnlySpan<byte> payload) => throw new NotSupportedException();
        public float GetJumpScale(in JumpRequest request) => throw new NotSupportedException();
        public float GetFrictionScale(int playerIndex, float friction, float speed, bool ducking, int groundMaterial) => throw new NotSupportedException();
        public float GetMaxSpeedScale(int playerIndex, float maxSpeed, bool ducking, int groundMaterial) => throw new NotSupportedException();
        public bool StandaloneCreate(EntityHandle entity, string className) => throw new NotSupportedException();
        public void StandaloneSpawn(EntityHandle entity) => throw new NotSupportedException();
        public int StandaloneDraw(EntityHandle entity) => throw new NotSupportedException();
        public int StandaloneDrawExtras(EntityHandle entity, int flags) => throw new NotSupportedException();
        public void StandaloneRemoved(EntityHandle entity) => throw new NotSupportedException();
        public void StandaloneDataChanged(EntityHandle entity, ReadOnlySpan<byte> blob, int updateType) => throw new NotSupportedException();
        public void OnStage(ClientFrameStage stage) => throw new NotSupportedException();
        public void InvalidateMdlCache() => throw new NotSupportedException();
    }

    private readonly Crossings _crossings = new();
    private readonly BatchClient _client = new();
    private readonly StandaloneClientBinding _binding;

    public StandaloneDrawBatchTests()
        => _binding = new(_crossings, new RecordingLog(), _client, new StandaloneMirror(), NetStateCatalog.Of([]));

    [Fact]
    public void AnUnpreparedEntityIsAnsweredMinusOneOverWhatNativeLeftInItsSlot()
    {
        uint[] handles = [2, 3, 4, 7];
        int[] results = [77, 77, 77, 77];
        fixed (uint* h = handles)
        fixed (int* r = results)
            _binding.OnStandaloneDrawBatch(h, handles.Length, r);

        Assert.Equal([1, -1, 1, -1], results);
        Assert.Equal([2u, 3u, 4u, 7u], _client.Seen);
    }

    [Fact]
    public void AWholeViewIsOneCrossing()
    {
        uint[] handles = [.. Enumerable.Range(0, 40).Select(i => (uint)i)];
        int[] results = new int[handles.Length];
        long before = _crossings.Read(CrossingOp.ClientStandaloneDrawBatch);
        fixed (uint* h = handles)
        fixed (int* r = results)
            _binding.OnStandaloneDrawBatch(h, handles.Length, r);

        Assert.Equal(before + 1, _crossings.Read(CrossingOp.ClientStandaloneDrawBatch));
        Assert.Equal(40, _client.Seen.Count);
    }

    [Fact]
    public void AnEmptyOrNullBatchCountsItsCrossingAndNeverReachesTheModule()
    {
        uint h = 2;
        int r = 77;
        _binding.OnStandaloneDrawBatch(null, 1, &r);
        _binding.OnStandaloneDrawBatch(&h, 1, null);
        _binding.OnStandaloneDrawBatch(&h, 0, &r);

        Assert.Equal(3, _crossings.Read(CrossingOp.ClientStandaloneDrawBatch));
        Assert.Equal(0, _client.Batches);
        Assert.Equal(77, r);
    }
}
