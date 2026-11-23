extern alias client;

using System.Reflection;

using SourceSharp.Abi;

using Xunit;

using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientStandaloneEntity = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;
using ClientVendor = client::TF2.Descent.Client.Town.ClientVendor;

namespace TF2.Descent.Tests.Town;

/// <summary>A stall's client loops its idle, and plays a once-only sequence (the locker's doors) once.</summary>
public sealed class ClientVendorTests
{
    private static ClientVendor Vendor(ClientEngine engine)
    {
        var v = new ClientVendor(engine);
        typeof(ClientStandaloneEntity).GetProperty("Index")!.SetValue(v, 91);
        typeof(ClientStandaloneEntity).GetProperty("Handle")!.SetValue(v, new EntityHandle((uint)((7 << 16) | 91)));
        typeof(ClientStandaloneEntity).GetProperty("Entities", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(v, engine);
        v.State.BindLocal();
        return v;
    }

    [Fact]
    public void An_idle_loops()
    {
        var engine = new ClientEngine();
        var vendor = Vendor(engine);
        vendor.State.Sequence = 3;

        vendor.OnDataChanged(default, DataUpdateType.DataTableChanged);

        var play = Assert.Single(engine.AllOf("EntityRender.PlaySequence"));
        Assert.Equal(3, play.ArgAs<int>(1));
        Assert.True(play.ArgAs<bool>(3));
    }

    [Fact]
    public void A_once_only_sequence_plays_once_and_the_next_one_plays_again()
    {
        var engine = new ClientEngine();
        var vendor = Vendor(engine);
        vendor.State.Sequence = 1;
        vendor.State.Once = true;
        vendor.OnDataChanged(default, DataUpdateType.DataTableChanged);
        vendor.State.Sequence = 2;
        vendor.OnDataChanged(default, DataUpdateType.DataTableChanged);

        var plays = engine.AllOf("EntityRender.PlaySequence").ToList();
        Assert.Equal([1, 2], plays.Select(p => p.ArgAs<int>(1)));
        Assert.All(plays, p => Assert.False(p.ArgAs<bool>(3)));
    }
}
