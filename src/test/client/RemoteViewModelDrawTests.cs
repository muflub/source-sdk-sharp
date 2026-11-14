using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Another player's viewmodel and sleeve draw nothing, whatever a spawn did: both are sent
/// to everyone, so a client that drew one stood a pair of arms beside every remote player.
/// </summary>
public class RemoteViewModelDrawTests
{
    private const string CustomDraw = "EntityRender.SetCustomDraw";

    [Fact]
    public void A_remote_viewmodels_decode_says_it_draws_nothing()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", 1);
        var viewModel = new ClientSsViewModel(engine, engine) { Handle = new EntityHandle(0x00010042u) };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = 2;

        viewModel.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.Single(engine.AllOf(CustomDraw));
    }

    [Fact]
    public void A_remote_viewmodels_later_decode_says_it_again()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", 1);
        var viewModel = new ClientSsViewModel(engine, engine) { Handle = new EntityHandle(0x00010042u) };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = 2;

        viewModel.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        viewModel.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.Equal(2, engine.AllOf(CustomDraw).Count());
    }

    [Fact]
    public void The_local_viewmodel_is_left_to_its_pass()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", 1);
        var viewModel = new ClientSsViewModel(engine, engine) { Handle = new EntityHandle(0x00010042u) };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = 1;

        viewModel.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.Empty(engine.AllOf(CustomDraw));
    }

    [Fact]
    public void A_remote_sleeves_decode_says_it_draws_nothing()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", 1);
        var sleeve = new ClientSsViewmodelAttachment(engine, engine) { Handle = new EntityHandle(0x00010043u) };
        sleeve.State.BindLocal();
        sleeve.State.OwnerIndex = 2;

        sleeve.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.Single(engine.AllOf(CustomDraw));
    }
}
