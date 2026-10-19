using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's halves of EngineSide and the engine bindings
/// (plan_shim_areas_split phase 7): the client's EngineSide attaches only the
/// client's areas, and a server-only member of a shared seam answers here what
/// its attach guard answered on a client, where the server's areas are never
/// attached, without calling the engine.
/// </summary>
public class EngineSideClientHalfTests
{
    private static readonly EntityHandle Prop = new(0x0004_0021u);

    private static T Binding<T>(EngineSide side) where T : class
    {
        var services = new ServiceContainer().AddInstance(new StandaloneMirror());
        EngineServices.AddTo(services, side);
        return services.Require<T>();
    }

    private static EngineSide Attached(FakeShimRoot root)
    {
        var side = new EngineSide(Side.Client);
        side.Initialize();
        Assert.True(side.AttachOwnShims(root.Bind(), out string why), why);
        return side;
    }

    // The bindings are composed over a side that never attached: the answers
    // below are the client half's own and do not depend on the attach, and a
    // client composition over a fake root reaches slots the fake leaves null.
    private static EngineSide Client()
    {
        var side = new EngineSide(Side.Client);
        side.Initialize();
        return side;
    }

    [Fact]
    public void TheClientsEngineSideAttachesTheClientsAreasOnly()
    {
        using var root = new FakeShimRoot();
        EngineSide side = Attached(root);
        Assert.True(side.ShimsAttached, "AttachOwnShims attaches the client's areas");
        Assert.NotNull(side.Areas.Resolve(typeof(HudClientArea)));
        Assert.Null(side.Areas.Resolve(typeof(EntitiesServerArea)));
    }

    private const System.Reflection.BindingFlags Flags
        = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

    [Fact]
    public void AServerOnlyReadAnswersTheGuardsValue()
    {
        var entities = Binding<IEngineEntities>(Client());

        Assert.Equal(0, entities.GetHealth(Prop));
        Assert.Equal(EntityHandle.None, entities.GetParent(Prop));
        Assert.Equal((Vector.Zero, Vector.Zero), entities.GetBounds(Prop));
        Assert.Equal(EntityHandle.None, Binding<IEnginePlayers>(Client()).FindByUserId(7));
        Assert.Empty(entities.FindAllByClassname("prop_physics"));
        Assert.Empty(Binding<IEngineScripted>(Client()).TemplateSpawnKeyed([]));
    }

    [Fact]
    public void AServerOnlyOutReadZeroesItsOuts()
    {
        var networking = Binding<IEngineNetworking>(Client());

        Assert.Equal(EntityState.None, networking.EntityGetState(Prop, out MoveType moveType));
        Assert.Equal(MoveType.None, moveType);
    }

    /// <summary>
    /// The attachment frame's shared half asks the skeletons first; with none,
    /// the client's helper answers no frame, as the server's slot's guard did.
    /// </summary>
    [Fact]
    public void AnAttachmentFrameWithNoSkeletonIsNotFound()
    {
        var entities = Binding<IEngineEntities>(Client());

        Assert.False(entities.GetAttachmentFrame(Prop, 1, out Vector origin, out QAngle angles));
        Assert.Equal(default, origin);
        Assert.Equal(default, angles);
    }

    [Fact]
    public void ABoxIsNeverInAnInvalidPlayersPvs()
    {
        Assert.False(Binding<IEnginePlayers>(Client()).BoxInPvs(Prop, Vector.Zero, Vector.Zero));
    }

    /// <summary>
    /// Removal asks the side's removal routes on the client too: that half of
    /// the server's member is shared, and only the shell's removal is the
    /// server's.
    /// </summary>
    [Fact]
    public void ARemovalStillAsksTheClientsRemovalRoutes()
    {
        var side = Client();
        var asked = new List<EntityHandle>();
        side.Removal.Bind(h => { asked.Add(h); return RemovalMark.NotMine; }, () => 0);

        Binding<IEngineEntities>(side).Remove(Prop);

        Assert.Equal([Prop], asked);
    }
}
