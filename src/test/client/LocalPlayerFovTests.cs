using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;
using SourceSharp.Client.Standalone;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The view's field of view is the local player's: stock's CalcPlayerView ends
/// <c>fov = GetFOV()</c> (baseplayer_shared.cpp:1639), the client's GetFOV
/// (c_baseplayer.cpp:2437) is the networked words' ramp
/// (<see cref="PlayerFov.Current"/>), and the view setup writes it over the
/// default before Render scales it. The viewmodel rides the same setup's
/// offset, stock's <c>GetViewModelFOV() - ( default_fov - fov )</c> unchanged.
/// </summary>
public class LocalPlayerFovTests
{
    private const int Local = 2;

    private sealed class Directory : IClientStandalones
    {
        public readonly ClientEntityDirectory Entities = new();

        public IReadOnlyList<ClientStandaloneEntity> Live() => [];
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => Entities.ByIndex(entityIndex);
        public IEnumerable<ClientStandaloneEntity> OfClass(string? className) => Entities.OfClass(className);
        public IEnumerable<ClientStandaloneEntity> OfName(string? name) => Entities.OfName(name);
    }

    private static (RenderPlanHost Host, ClientSsPlayer Me, Directory Directory) Build(int localIndex = Local)
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", localIndex);
        engine.Returns("Clock.Now", 10f);

        var me = new ClientSsPlayer(engine) { Handle = new EntityHandle(0x0001_0000u | Local) };
        me.State.BindLocal();

        var directory = new Directory();
        if (localIndex > 0)
            directory.Entities.Bind(me.Handle, Local, me);

        return (new RenderPlanHost(Fov90(), new LocalPlayerFov(directory, engine, engine)), me, directory);
    }

    private static RenderConVars Fov90()
    {
        RenderConVars convars = RenderPlannerTests.ConVars();
        convars.Set("default_fov", 90f);
        return convars;
    }

    private static ViewSetUpOut SetUp(RenderPlanHost host, ClientSsPlayer me)
    {
        me.State.FovDefault = 90;
        var input = new ViewSetUpIn { ViewModelFov = 54f };
        host.ViewSetUp(in input, out ViewSetUpOut output);
        return output;
    }

    [Fact]
    public void AZoomedPlayersViewFovIsTheNetworkedFov()
    {
        var (host, me, _) = Build();
        me.State.Fov = 20;
        me.State.FovRate = 0f;

        Assert.Equal(20f, SetUp(host, me).Fov);
    }

    [Fact]
    public void AnUnzoomedFovGivesTheDefault()
    {
        var (host, me, _) = Build();
        me.State.Fov = 0;

        Assert.Equal(90f, SetUp(host, me).Fov);
    }

    [Fact]
    public void TheRampInterpolatesIntoTheView()
    {
        var (host, me, _) = Build();
        me.State.Fov = 20;
        me.State.FovStart = 90;
        me.State.FovTime = 9.5f;   // halfway through a one-second ramp at the clock's 10
        me.State.FovRate = 1f;

        Assert.Equal(55f, SetUp(host, me).Fov);
    }

    [Fact]
    public void PastTheRampTheViewFovIsTheTarget()
    {
        var (host, me, _) = Build();
        me.State.Fov = 20;
        me.State.FovStart = 90;
        me.State.FovTime = 5f;
        me.State.FovRate = 1f;

        Assert.Equal(20f, SetUp(host, me).Fov);
    }

    [Fact]
    public void TheViewmodelRidesTheSameOffset()
    {
        // Stock's post-CalcView algebra: GetViewModelFOV() - ( default_fov - fov )
        // == fov + (GetViewModelFOV() - default_fov), our setUp's offset.
        var (host, me, _) = Build();
        me.State.Fov = 20;
        me.State.FovRate = 0f;

        ViewSetUpOut output = SetUp(host, me);

        Assert.Equal(20f + (54f - 90f), output.Fov + output.ViewModelFovOffset);
    }

    [Fact]
    public void NoLocalPlayerLeavesTheDefault()
    {
        var (host, _, _) = Build(localIndex: 0);
        var input = new ViewSetUpIn { ViewModelFov = 54f };

        host.ViewSetUp(in input, out ViewSetUpOut output);

        Assert.Equal(90f, output.Fov);
    }
}
