using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client entity base carries all three save tiers across a reload
/// (plan_client_novalve §3.8): a <c>[SaveField]</c> or an
/// <see cref="ISaveCustom"/> on a client class that does not override
/// SaveState used to be dropped without a word.
/// </summary>
public class ClientSaveTierTests
{
    private static readonly EntityHandle Probe = new(0x0004_0021u);

    private sealed class Counter(IEngineEntityRender render) : ClientStandaloneEntity(render), ISaveCustom
    {
        public override string ClassName => "ss_counter";

        [SaveField] public int Thinks { get; set; }

        [SaveField] public string Label { get; set; } = "";

        public float Custom { get; set; }

        public void OnSave(ISaveWriter writer) => writer.Write("custom", Custom);

        public void OnRestore(ISaveReader reader) => Custom = reader.ReadFloat("custom");
    }

    private static Counter Bound(RecordingEngine engine)
    {
        var entity = new Counter(engine);
        entity.BindEngine(engine, engine);
        entity.Handle = Probe;
        return entity;
    }

    [Fact]
    public void A_marked_and_a_custom_member_come_back_through_the_base()
    {
        var engine = new RecordingEngine();
        engine.Returns("Entities.IsValid", true);

        var before = Bound(engine);
        before.Thinks = 41;
        before.Label = "kept";
        before.Custom = 2.5f;

        var record = new SavedEntity(Probe.Value, before.ClassName, string.Empty);
        before.SaveState(record);

        // A NEW instance, as a reload makes one: the type's defaults until the
        // record is loaded.
        var after = Bound(engine);
        Assert.Equal((0, "", 0f), (after.Thinks, after.Label, after.Custom));

        after.LoadState(record);

        Assert.Equal((41, "kept", 2.5f), (after.Thinks, after.Label, after.Custom));
        Assert.True(record.DefaultRan);
    }
}
