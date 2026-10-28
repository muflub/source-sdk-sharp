using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's schema gate (lane M1-C, from generic_payload_client.cpp's
/// SourceSharpSchema_Accept): a standalone entity's payload is decoded only
/// when its type was published here with the hash it arrived with; refusals
/// are counted per update, kept per type for the first sixteen types, and
/// warned once per kept type.
/// </summary>
public unsafe class ClientSchemaGateTests
{
    private readonly RecordingLog _log = new();
    private readonly ClientSchemaGate _gate;

    public ClientSchemaGateTests() => _gate = new ClientSchemaGate(_log);

    private int Warnings => _log.Lines.Count(l => l.Warning);

    [Fact]
    public void A_matching_hash_is_decoded_and_counts_nothing()
    {
        Assert.True(_gate.Accept(0x10, published: true, expected: 0xAA, seen: 0xAA, entIndex: 3, "ss_crate"));

        Assert.Equal((0, 0), (_gate.Refusals().RefusedUpdates, _gate.Refusals().Types.Count));
        Assert.Equal(0, Warnings);
    }

    [Fact]
    public void A_type_this_side_never_published_is_refused_and_not_counted()
    {
        Assert.False(_gate.Accept(0x10, published: false, expected: 0, seen: 0, entIndex: 3, ""));

        Assert.Equal((0, 0), (_gate.Refusals().RefusedUpdates, _gate.Refusals().Types.Count));
        Assert.Equal(0, Warnings);
    }

    [Fact]
    public void A_mismatch_is_refused_counted_and_kept_with_both_hashes()
    {
        Assert.False(_gate.Accept(0x10, published: true, expected: 0xAA, seen: 0xBB, entIndex: 3, "ss_crate"));

        var (updates, types) = _gate.Refusals();
        Assert.Equal(1, updates);
        Assert.Equal(new ClientSchemaRefusalRow(0x10, 0xAA, 0xBB, 1, "ss_crate"), Assert.Single(types));
        Assert.Contains("SCHEMA MISMATCH on entity 3: 'ss_crate' (type 0x00000010)", _log.Text);
        Assert.Contains("fingerprint 0x000000bb and this build's registry says 0x000000aa", _log.Text);
    }

    [Fact]
    public void A_type_is_warned_once_and_counted_every_update()
    {
        for (int i = 0; i < 5; i++)
            Assert.False(_gate.Accept(0x10, published: true, expected: 0xAA, seen: 0xBB, entIndex: 3, "ss_crate"));

        var (updates, types) = _gate.Refusals();
        Assert.Equal(5, updates);
        Assert.Equal(5, Assert.Single(types).Count);
        Assert.Equal(1, Warnings);
    }

    [Fact]
    public void Past_sixteen_types_a_refusal_is_counted_and_warned_but_not_kept()
    {
        for (uint t = 0; t < ClientSchemaGate.MaxTypes; t++)
            _gate.Accept(t, published: true, expected: 1, seen: 2, entIndex: 1, $"c{t}");

        // The seventeenth type, twice: not kept, so nothing remembers it was said.
        _gate.Accept(99, published: true, expected: 1, seen: 2, entIndex: 1, "late");
        _gate.Accept(99, published: true, expected: 1, seen: 2, entIndex: 1, "late");

        var (updates, types) = _gate.Refusals();
        Assert.Equal(ClientSchemaGate.MaxTypes + 2, updates);
        Assert.Equal(ClientSchemaGate.MaxTypes, types.Count);
        Assert.DoesNotContain(types, r => r.TypeId == 99);
        Assert.Equal(ClientSchemaGate.MaxTypes + 2, Warnings);
    }

    [Fact]
    public void The_status_command_lists_each_refused_type()
    {
        _gate.Accept(0x10, published: true, expected: 0xAA, seen: 0xBB, entIndex: 3, "ss_crate");
        _gate.Accept(0x10, published: true, expected: 0xAA, seen: 0xBB, entIndex: 3, "ss_crate");
        var printed = new RecordingLog();

        new ClientSchemaStatusCommand(printed, _gate, Types()).Execute("");

        Assert.Contains("2 refusal(s) over 1 type(s)", printed.Text);
        Assert.Contains("'ss_crate' (0x00000010): expected 0x000000aa, saw 0x000000bb, 2 update(s) refused", printed.Text);
        Assert.DoesNotContain("nothing has been refused", printed.Text);
    }

    [Fact]
    public void The_status_command_says_when_nothing_was_refused()
    {
        var printed = new RecordingLog();

        new ClientSchemaStatusCommand(printed, _gate, Types()).Execute("");

        Assert.Contains("0 refusal(s) over 0 type(s)", printed.Text);
        Assert.Contains("nothing has been refused", printed.Text);
    }

    private static ClientNetSchemaTypes Types()
        => new(new NoTiers(), () => []);

    private sealed class NoTiers : INetTiers
    {
        public IReadOnlyList<NetTierInfo> For(NetShellKind shell) => [];
        public IReadOnlyList<int> Sizes => [];
        public int Ceiling(int layoutBytes) => -1;
        public double Fill(int layoutBytes) => 0;
        public bool NearCeiling(int layoutBytes) => false;
    }

    // ---- the slot ----------------------------------------------------------

    private sealed class SchemaClient : IClientModule
    {
        public (uint TypeId, bool Published, uint Expected, uint Seen, int EntIndex, string ClassName)? Asked;
        public bool Answer;

        public bool SchemaAccept(uint typeId, bool published, uint expected, uint seen, int entIndex, string className)
        {
            Asked = (typeId, published, expected, seen, entIndex, className);
            return Answer;
        }

        // Not this slot's: a call here is a wiring fault, never a default answer.
        public void StandaloneDrawBatch(ReadOnlySpan<uint> handles, Span<int> results) => throw new NotSupportedException();
        public void DispatchTempEntity(int handle, ReadOnlySpan<byte> blob) => throw new NotSupportedException();
        public bool ForwardTempEntity(int id, ReadOnlySpan<float> floats, ReadOnlySpan<int> ints) => throw new NotSupportedException();
        public void TempEntPoolUpdate(float frameTime) => throw new NotSupportedException();
        public void TempEntPoolClear() => throw new NotSupportedException();
        public int ImpactEffectParams(int effect, int material, float scale, bool noFlecks, Span<float> block) => throw new NotSupportedException();
        public int ImpactChoice(ReadOnlySpan<int> input, Span<int> output, Span<byte> particleName, Span<byte> decalName) => throw new NotSupportedException();
        public bool SuppressLocalTempEnt(bool canPredict, int recipientCount) => throw new NotSupportedException();
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

    [Fact]
    public void The_slot_hands_the_module_every_fact_and_carries_its_answer()
    {
        var crossings = new Crossings();
        var client = new SchemaClient { Answer = true };
        var binding = new StandaloneClientBinding(crossings, new RecordingLog(), client, new StandaloneMirror(), NetStateCatalog.Of([]));
        byte[] name = "ss_crate\0"u8.ToArray();

        int answer;
        fixed (byte* p = name)
            answer = binding.SchemaAccept(0x10, 1, 0xAA, 0xBB, 3, p);

        Assert.Equal(1, answer);
        Assert.Equal((0x10u, true, 0xAAu, 0xBBu, 3, "ss_crate"), client.Asked);
        Assert.Equal(1, crossings.Read(CrossingOp.ClientSchemaAccept));

        client.Answer = false;
        fixed (byte* p = name)
            Assert.Equal(0, binding.SchemaAccept(0x10, 0, 0, 0xBB, 3, p));
        Assert.False(client.Asked!.Value.Published);
    }
}
