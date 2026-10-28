using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Render;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// FormatViewModelAttachment (c_baseviewmodel.cpp) and the local viewmodel's
/// attachments for the world's pass: the screen axes scale by
/// tan(fov/2) / tan(fovViewmodel/2), the depth along the view is kept, and
/// the inverse undoes it. A 90 degree world over a 54 degree viewmodel is a
/// factor of 1 / tan(27 degrees), 1.96261.
/// </summary>
public class ViewModelFovTests
{
    // Eye at the origin looking along +X: right is -Y, up is +Z.
    private static readonly PlayerView Eye = new(new Vector(0, 0, 0), new Vector(0, 0, 0), 90f, 54f);

    private const float Factor = 1.96261f;

    private static void Near(Vector expected, Vector actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Z, actual.Z, 3);
    }

    [Fact]
    public void TheForwardConversionScalesTheScreenAxesByTheRatioOfTheTangents()
        => Near(new Vector(10, -1 * Factor, 2 * Factor), ViewModelFov.Format(new Vector(10, -1, 2), false, Eye));

    [Fact]
    public void TheInverseUndoesTheForwardConversion()
    {
        Vector point = new(10, -1, 2);

        Near(point, ViewModelFov.Format(ViewModelFov.Format(point, false, Eye), true, Eye));
    }

    [Fact]
    public void TheInverseDividesByTheFactor()
        => Near(new Vector(10, -1 / Factor, 2 / Factor), ViewModelFov.Format(new Vector(10, -1, 2), true, Eye));

    [Fact]
    public void ThePointIsMeasuredFromTheEyeAlongItsOwnAxes()
    {
        // Looking along +Y (yaw 90): right is +X, and the eye is moved off the origin.
        var eye = Eye with { Origin = new Vector(5, 5, 5), Angles = new Vector(0, 90, 0) };

        Near(new Vector(5 + Factor, 15, 5), ViewModelFov.Format(new Vector(6, 15, 5), false, eye));
    }

    [Fact]
    public void AViewmodelFovOfZeroPutsTheInverseOnTheViewAxis()
        => Near(new Vector(10, 0, 0), ViewModelFov.Format(new Vector(10, -1, 2), true, Eye with { FovViewmodel = 0f }));

    //-------------------------------------------------------------------------
    // The local viewmodel's attachments for the world's pass.
    //-------------------------------------------------------------------------

    private sealed class One(int index, ClientStandaloneEntity entity) : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live() => [entity];

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => entityIndex == index ? entity : null;
    }

    private static readonly EntityHandle Vm = new(0x00010042u);

    private static ClientSsViewModel ViewModel(int owner)
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", 1);
        var viewModel = new ClientSsViewModel(engine, engine) { Handle = Vm };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = owner;
        viewModel.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        return viewModel;
    }

    private static PlayerViewRecord Known()
    {
        var record = new PlayerViewRecord();
        record.Record(Eye);
        return record;
    }

    [Fact]
    public void TheLocalViewmodelsAttachmentIsConvertedToTheWorldsFieldOfView()
    {
        var format = new ViewModelAttachmentFormat(new One(0x42, ViewModel(owner: 1)), Known());

        Near(new Vector(10, -1 * Factor, 2 * Factor), format.ForWorldPass(Vm, new Vector(10, -1, 2)));
    }

    [Fact]
    public void SomebodyElsesViewmodelIsNotConverted()
    {
        var format = new ViewModelAttachmentFormat(new One(0x42, ViewModel(owner: 2)), Known());

        Assert.Equal(new Vector(10, -1, 2), format.ForWorldPass(Vm, new Vector(10, -1, 2)));
    }

    [Fact]
    public void NothingIsConvertedBeforeAViewIsKnown()
    {
        var format = new ViewModelAttachmentFormat(new One(0x42, ViewModel(owner: 1)), new PlayerViewRecord());

        Assert.Equal(new Vector(10, -1, 2), format.ForWorldPass(Vm, new Vector(10, -1, 2)));
    }

    [Fact]
    public void AnotherEntityInTheViewmodelsSlotIsNotConverted()
    {
        var format = new ViewModelAttachmentFormat(new One(0x42, ViewModel(owner: 1)), Known());

        Assert.Equal(new Vector(10, -1, 2), format.ForWorldPass(new EntityHandle(0x00020042u), new Vector(10, -1, 2)));
    }
}
