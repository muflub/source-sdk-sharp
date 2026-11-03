using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The client half of the viewmodel attachment: the weapon's own model rides
/// the local player's hands bonemerged (stock's C_ViewmodelAttachmentModel,
/// drawn in the viewmodel pass), and belongs to nobody else.
/// </summary>
public class ViewmodelAttachmentTests
{
    private const int Local = 1;
    private const int ViewmodelIndex = 0x42;

    private static readonly EntityHandle Attachment = new(0x0001_0000u | 0x43);

    private static (ClientSsViewmodelAttachment Attachment, RecordingEngine Engine) Decoded(int owner, int viewModel)
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", Local);
        var attachment = new ClientSsViewmodelAttachment(engine, engine) { Handle = Attachment };
        attachment.State.BindLocal();
        attachment.State.OwnerIndex = owner;
        attachment.State.ViewModelIndex = viewModel;
        attachment.OnSpawn();
        attachment.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        return (attachment, engine);
    }

    private static bool MergedOnto(RecordingEngine engine, int source)
        => engine.Calls.Any(c => c.Member == "EntityRender.SetBoneMergeSource" &&
                                 c.ArgAs<EntityHandle>(0) == Attachment &&
                                 c.ArgAs<int>(1) == source);

    [Fact]
    public void TheLocalAttachmentJoinsTheViewmodelPassAndMergesOntoTheViewmodel()
    {
        var (_, engine) = Decoded(owner: Local, viewModel: ViewmodelIndex);

        Assert.True(engine.Calls.Any(c => c.Member == "EntityRender.SetViewModel" &&
                                          c.ArgAs<EntityHandle>(0) == Attachment &&
                                          c.ArgAs<bool>(1)),
            "the attachment draws in the viewmodel pass, at the eyes");
        Assert.True(MergedOnto(engine, ViewmodelIndex), "and rides the viewmodel's skeleton");
    }

    [Fact]
    public void AnAttachmentOfSomebodyElseDrawsNothingAndMergesNothing()
    {
        var (_, engine) = Decoded(owner: 2, viewModel: ViewmodelIndex);

        Assert.True(!engine.Calls.Any(c => c.Member == "EntityRender.SetViewModel" &&
                                           c.ArgAs<bool>(1)),
            "not in the pass: a pair of arms inside every other player is worse than none");
        Assert.True(!engine.Calls.Any(c => c.Member == "EntityRender.SetBoneMergeSource" &&
                                           c.ArgAs<int>(1) >= 0),
            "and merged onto nothing");
        Assert.True(engine.Calls.Any(c => c.Member == "EntityRender.SetCustomDraw" &&
                                          c.ArgAs<EntityHandle>(0) == Attachment),
            "it draws nothing");
    }

    [Fact]
    public void TheNamedViewmodelIsMergedOntoBeforeItHasArrived()
    {
        // Live, the attachment decoded once, before the viewmodel was known to
        // be local, and nothing decoded it again: it drew at the eye in its
        // own reference pose. The merge names the index; the bones resolve it.
        var (_, engine) = Decoded(owner: Local, viewModel: ViewmodelIndex);

        Assert.True(MergedOnto(engine, ViewmodelIndex),
            "one decode is enough: no live viewmodel needs to be found first");
    }

    [Fact]
    public void TheMergeFollowsTheViewmodelItRides()
    {
        var (attachment, engine) = Decoded(owner: Local, viewModel: ViewmodelIndex);

        // A new viewmodel entity (the old one gone, the server re-created the
        // pair): the next update points the merge at the new index.
        attachment.State.ViewModelIndex = 0x51;
        attachment.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.True(MergedOnto(engine, 0x51),
            "the merge follows the viewmodel it rides, not the first one it saw");
    }

    [Fact]
    public void LeavingTheLocalEyeLeavesThePassAndTheSkeleton()
    {
        var (attachment, engine) = Decoded(owner: Local, viewModel: ViewmodelIndex);

        attachment.State.OwnerIndex = 2;
        attachment.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.True(engine.Calls.Any(c => c.Member == "EntityRender.SetViewModel" &&
                                          c.ArgAs<EntityHandle>(0) == Attachment &&
                                          !c.ArgAs<bool>(1)),
            "out of the pass");
        Assert.True(MergedOnto(engine, -1), "and off the skeleton");
    }

    //-------------------------------------------------------------------------
    // The viewer's own state: the sleeve rides the arms, so it obeys the same
    // rule (stock's C_ViewmodelAttachmentModel draws from the viewmodel pass).
    //-------------------------------------------------------------------------

    /// <summary>A directory holding the viewer and his attachment: GetByIndex answers by bound index.</summary>
    private sealed class ViewerDirectory : IClientStandalones
    {
        public readonly List<ClientStandaloneEntity> Entities = [];
        public IReadOnlyList<ClientStandaloneEntity> Live() => Entities;
        public ClientStandaloneEntity? GetByIndex(int entityIndex)
            => Entities.FirstOrDefault(e => e.Index == entityIndex);
    }

    private static (ClientSsViewmodelAttachment Attachment, ClientSsPlayer Viewer, RecordingEngine Engine)
        ViewerRig(bool alive)
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", Local);

        var directory = new ViewerDirectory();

        var viewer = new ClientSsPlayer(engine, screen: engine, entities: directory)
        {
            Handle = new EntityHandle(0x0001_0000u | Local),
        };
        viewer.BindIndex(Local);
        viewer.State.BindLocal();
        viewer.State.Alive = alive;
        directory.Entities.Add(viewer);

        var attachment = new ClientSsViewmodelAttachment(engine, engine, directory)
        {
            Handle = Attachment,
        };
        attachment.State.BindLocal();
        attachment.State.OwnerIndex = Local;
        attachment.State.ViewModelIndex = ViewmodelIndex;
        directory.Entities.Add(attachment);

        return (attachment, viewer, engine);
    }

    [Fact]
    public void TheDeadViewersSleeveEntersNoPassAndMergesOntoNothing()
    {
        var (attachment, _, engine) = ViewerRig(alive: false);
        attachment.OnSpawn();
        attachment.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.True(!engine.Calls.Any(c => c.Member == "EntityRender.SetViewModel" &&
                                           c.ArgAs<bool>(1)),
            "the dead viewer's sleeve never joins the pass — the bug showed the arm AND the sleeve");
        Assert.True(!engine.Calls.Any(c => c.Member == "EntityRender.SetBoneMergeSource" &&
                                           c.ArgAs<int>(1) >= 0),
            "and never rides a skeleton");
    }

    [Fact]
    public void TheKilledViewerDropsTheSleeveAndTheRespawnPicksItBackUp()
    {
        var (attachment, viewer, engine) = ViewerRig(alive: true);
        attachment.OnSpawn();
        attachment.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.True(MergedOnto(engine, ViewmodelIndex), "the rig: merged while alive");

        viewer.State.Alive = false;
        viewer.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.True(MergedOnto(engine, -1),
            "the player's death decode re-ran the decision — the sleeve un-merges");
        Assert.True(engine.Calls.Any(c => c.Member == "EntityRender.SetViewModel" &&
                                          c.ArgAs<EntityHandle>(0) == Attachment &&
                                          !c.ArgAs<bool>(1)),
            "and leaves the pass");

        viewer.State.Alive = true;
        viewer.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.True(MergedOnto(engine, ViewmodelIndex),
            "the respawn decode puts it back on the arms it rides — a gate with no re-entry is a missing sleeve");
        Assert.True(engine.Calls.Count(c => c.Member == "EntityRender.SetViewModel" &&
                                            c.ArgAs<EntityHandle>(0) == Attachment &&
                                            c.ArgAs<bool>(1)) == 2,
            "in, out, and back in: the pair re-entered");
    }
}
