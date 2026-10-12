extern alias client;
using SourceSharp.Abi;
using client::SourceSharp.Client;
using client::SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A client entity that records the ORDER of everything it is told, so the
/// ordering can be asserted rather than described.
/// </summary>
[ClientStandaloneEntity("test_create_order")]
public sealed class TestCreateOrderEntity : ClientStandaloneEntity
{
    public TestCreateOrderEntity(IEngineEntityRender render) : base(render)
        => Steps.Add("constructed");

    public List<string> Steps { get; } = new();

    public override string ClassName => "test_create_order";

    public override void OnSpawn() => Steps.Add("spawn");

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
        => Steps.Add($"data:{updateType}:{blob.Length}");
}

/// <summary>
/// AN ENTITY EXISTS IN MANAGED CODE BEFORE ITS FIRST DATA UPDATE — step 5c's
/// second gate, from the managed side.
///
/// <para>
/// The native half of the same gate is in
/// <c>sourcesharp/test/standalone_client_layout_test.cpp</c>: the type id's
/// recv proxy makes the managed half DURING the decode, ahead of the payload
/// words that carry the entity's own state, because the engine decodes the
/// whole first update before it calls <c>PostDataUpdate</c>. That side can
/// only be checked textually.
/// </para>
///
/// <para>
/// THIS SIDE CAN BE CHECKED MECHANICALLY, and this is the half that says what
/// the ordering is FOR: state handed to an entity that does not exist yet
/// reaches nothing at all, silently. Not an exception, not a warning — the
/// registry answers null and the update evaporates. So "the entity is created
/// first" is not tidiness; it is the difference between a class seeing its
/// first update and never seeing it.
/// </para>
/// </summary>
public class ClientStandaloneCreateOrderTests
{
    private static readonly EntityHandle Ent = new(0x0705_0001u);

    private static ClientStandaloneRegistry Registry()
    {
        var registry = new ClientStandaloneRegistry(ScriptedEntityRegistry.UnattachedEngine());

        registry.RegisterAll(typeof(TestCreateOrderEntity).Assembly);
        registry.Services = Services(out var engine);

        // The one ask the registry makes per entity (Phase X4). This suite is
        // about ordering, not about the index.
        engine.Ignores("Entities.GetIndex");

        return registry;
    }

    [Fact]
    public void AnEntityThatWasNeverCreatedIsNotThere()
        => Assert.Null(Registry().Get(Ent));

    [Fact]
    public void DataForAnEntityThatWasNeverCreatedReachesNothing()
    {
        // THE FAILURE THE ORDERING PREVENTS, stated as the fact it is. There is
        // no exception and no refusal here — ClientModule.StandaloneDataChanged
        // is `_standalones.Get(entity)?.DispatchDataChanged(...)`, so an update
        // for an entity managed code has not made yet is DISCARDED WITHOUT A
        // WORD. That is why the shell creates the managed half from the type
        // id's recv proxy rather than after the update has decoded.
        var registry = Registry();

        Assert.Null(registry.Get(Ent));

        // The call ClientModule makes, spelled the same way, against nothing.
        registry.Get(Ent)?.DispatchDataChanged(new byte[16], DataUpdateType.Created);

        Assert.True(registry.Create(Ent, "test_create_order"));

        var entity = Assert.IsType<TestCreateOrderEntity>(registry.Get(Ent));

        Assert.Equal(new[] { "constructed" }, entity.Steps);
    }

    [Fact]
    public void TheEntityExistsBeforeItsFirstDataUpdate()
    {
        var registry = Registry();

        Assert.True(registry.Create(Ent, "test_create_order"));

        var entity = Assert.IsType<TestCreateOrderEntity>(registry.Get(Ent));

        entity.DispatchDataChanged(new byte[16], DataUpdateType.Created);

        // The order, not the end state. Both orders leave an entity in the
        // registry; only one of them leaves an entity that saw its own first
        // update.
        Assert.Equal(new[] { "constructed", "data:Created:16" }, entity.Steps);
    }

    [Fact]
    public void SpawnPrecedesTheFirstDataUpdate()
    {
        // The shell's order, mirrored: PostDataUpdate pushes dormancy, fires
        // OnStandaloneSpawn, and only then hands the blob over. A class that
        // set itself up in OnSpawn must be set up before its first values
        // arrive, or the first update is applied to a half-built object.
        var registry = Registry();

        Assert.True(registry.Create(Ent, "test_create_order"));

        var entity = Assert.IsType<TestCreateOrderEntity>(registry.Get(Ent));

        entity.OnSpawn();
        entity.DispatchDataChanged(new byte[16], DataUpdateType.Created);

        Assert.Equal(new[] { "constructed", "spawn", "data:Created:16" }, entity.Steps);
    }

    [Fact]
    public void CreatingTheSameHandleTwiceKeepsTheFirstEntity()
    {
        // WHAT THE SHELL'S EnsureManagedHalf RESTS ON. It is called from two
        // places — the type id's recv proxy and the top of PostDataUpdate — and
        // both may run for one update. The shell guards on m_bHasManaged; this
        // is the registry's own half of the same guarantee, so that a guard
        // lost on the native side is a duplicated OBJECT rather than a
        // duplicated entity.
        var registry = Registry();

        Assert.True(registry.Create(Ent, "test_create_order"));

        var first = registry.Get(Ent);

        Assert.True(registry.Create(Ent, "test_create_order"));

        Assert.Same(first, registry.Get(Ent));
    }

    [Fact]
    public void AClassnameNothingClaimsIsRefusedRatherThanInvented()
    {
        // The ordinary case, and the reason the shell treats a refusal as
        // normal: most entities on this hierarchy are server-only as far as
        // logic goes and have no managed half here BY DESIGN. If this answered
        // true, EnsureManagedHalf would set m_bHasManaged for every entity and
        // the blob would be handed to a class that does not exist.
        Assert.False(Registry().Create(Ent, "test_create_order_no_such_class"));
    }
}
