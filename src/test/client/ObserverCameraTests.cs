using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;
using SourceSharp.Client.Standalone;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ObserverCamera"/> (plan_client_novalve C7 item 5): the local
/// player's networked observer fields, once a frame, into the view native's
/// OverrideView draws -- and handed back the frame observing stops.
/// </summary>
public class ObserverCameraTests
{
    private const int Local = 2, Watched = 1;

    private sealed class Slots : IObserverCameraSlots
    {
        public readonly List<(bool Active, Vector Origin, QAngle Angles, bool SetAngles)> Views = [];
        public Dictionary<int, EntityRenderPose> Poses = [];

        public CameraTrace TraceHull(Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask, TraceFilter filter, int passIndex)
            => new(false, 1f, end);

        public EntityRenderPose? RenderPose(int entityIndex)
            => Poses.TryGetValue(entityIndex, out var p) ? p : null;

        public void SetView(bool active, Vector origin, QAngle angles, bool setViewAngles)
            => Views.Add((active, origin, angles, setViewAngles));
    }

    private sealed class Directory : IClientStandalones
    {
        public readonly ClientEntityDirectory Entities = new();

        public readonly List<ClientStandaloneEntity> Carried = [];

        public IReadOnlyList<ClientStandaloneEntity> Live() => Carried;
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => Entities.ByIndex(entityIndex);
        public IEnumerable<ClientStandaloneEntity> OfClass(string? className) => Entities.OfClass(className);
        public IEnumerable<ClientStandaloneEntity> OfName(string? name) => Entities.OfName(name);
    }

    private sealed class Wishes : IRenderPolicyWishes
    {
        public void WantTwoPass(EntityHandle entity, bool twoPass) { }
        public void WantTransparent(EntityHandle entity, bool transparent) { }
        public void WantOwnerSeen(EntityHandle entity, bool ownerSeen) { }
        public int InEyeTarget { get; private set; }
        public readonly List<int> Watched = [];
        public void WatchInEye(int index)
        {
            InEyeTarget = index;
            Watched.Add(index);
        }
    }

    /// <summary>A carried entity whose wish follows the in-eye target.</summary>
    private sealed class Gated(IEngineEntityRender render) : ClientStandaloneEntity(render), IFirstPersonGated
    {
        public int Asked;
        public override string ClassName => "gated";
        public void ReconsiderFirstPerson() => Asked++;
    }

    private static (ObserverCamera Camera, Slots Slots, ClientSsPlayer Me) Build() => Build(new Wishes());

    private static (ObserverCamera Camera, Slots Slots, ClientSsPlayer Me) Build(Wishes wishes, Gated? gated = null)
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", Local);
        engine.Returns("Clock.Now", 10f);

        var me = new ClientSsPlayer(engine) { Handle = new EntityHandle(0x0001_0000u | Local) };
        me.State.BindLocal();
        me.State.Alive = true;
        me.State.ViewOffset = new Vector(0, 0, 64);
        var them = new ClientSsPlayer(engine) { Handle = new EntityHandle(0x0001_0000u | Watched) };
        them.State.BindLocal();
        them.State.Alive = true;
        them.State.ViewOffset = new Vector(0, 0, 64);

        var directory = new Directory();
        directory.Entities.Bind(me.Handle, Local, me);
        directory.Entities.Bind(them.Handle, Watched, them);
        if (gated is not null)
            directory.Carried.Add(gated);

        var slots = new Slots();
        slots.Poses[Watched] = new EntityRenderPose(new Vector(100, 200, 0), new QAngle(0, 45, 0), true);

        var camera = new ObserverCamera(slots, directory, engine, new ViewReadout(), engine, DeclaredConVars.Holding<ClForceCameraConVar>("1"), wishes);
        return (camera, slots, me);
    }

    [Fact]
    public void APlayerWhoIsNotObservingHandsNothingOver()
    {
        var (camera, slots, _) = Build();
        camera.Pass();
        Assert.Empty(slots.Views);
        Assert.Null(camera.Last);
    }

    [Fact]
    public void InEyeTellsTheRenderPolicyWhoIsInFirstPerson()
    {
        // C_BasePlayer::InFirstPersonView: the in-eye target is not drawn from
        // inside its own eyes; chase hands it back to the third person.
        var wishes = new Wishes();
        var (camera, _, me) = Build(wishes);
        me.State.ObserverMode = (int)ObserverMode.InEye;
        me.State.ObserverTarget = 0x0001_0000u | Watched;

        camera.Pass();
        camera.Pass();
        me.State.ObserverMode = (int)ObserverMode.Chase;
        camera.Pass();

        Assert.Equal([Watched, 0], wishes.Watched);
    }

    [Fact]
    public void ANewInEyeTargetAsksWhatThePlayersCarryAgain()
    {
        // The carried weapon and worn items decide their wish on a decode, and
        // nothing of theirs moved: the camera asks them, once per change.
        var gated = new Gated(Fake());
        var (camera, _, me) = Build(new Wishes(), gated);
        me.State.ObserverMode = (int)ObserverMode.InEye;
        me.State.ObserverTarget = 0x0001_0000u | Watched;

        camera.Pass();
        camera.Pass();

        Assert.Equal(1, gated.Asked);
    }

    [Fact]
    public void LeavingObservingWatchesNobodyInEye()
    {
        var wishes = new Wishes();
        var (camera, _, me) = Build(wishes);
        me.State.ObserverMode = (int)ObserverMode.InEye;
        me.State.ObserverTarget = 0x0001_0000u | Watched;
        camera.Pass();

        me.State.ObserverMode = (int)ObserverMode.None;
        camera.Pass();

        Assert.Equal(0, wishes.InEyeTarget);
    }

    [Fact]
    public void InEyeOnTheWatchedPlayerHandsOverItsEye()
    {
        var (camera, slots, me) = Build();
        me.State.ObserverMode = (int)ObserverMode.InEye;
        me.State.ObserverTarget = 0x0001_0000u | Watched;

        camera.Pass();

        var view = Assert.Single(slots.Views);
        Assert.True(view.Active);
        Assert.Equal(new Vector(100, 200, 64), view.Origin);
        Assert.True(view.SetAngles);
        Assert.Equal(1, camera.Frames);
    }

    [Fact]
    public void StoppingObservingHandsTheViewBackOnce()
    {
        var (camera, slots, me) = Build();
        me.State.ObserverMode = (int)ObserverMode.InEye;
        me.State.ObserverTarget = 0x0001_0000u | Watched;
        camera.Pass();

        me.State.ObserverMode = (int)ObserverMode.None;
        camera.Pass();
        camera.Pass();

        Assert.Equal(2, slots.Views.Count);
        Assert.False(slots.Views[1].Active);
        Assert.Null(camera.Last);
    }

    [Fact]
    public void ATargetWithNoDrawnPoseIsNoTarget()
    {
        // The target is not on this client (outside its PVS, say): in-eye with
        // no target is the observer's own eye (CalcInEyeCamView's first arm).
        var (camera, slots, me) = Build();
        slots.Poses.Clear();
        me.State.ObserverMode = (int)ObserverMode.InEye;
        me.State.ObserverTarget = 0x0001_0000u | Watched;

        camera.Pass();

        var view = Assert.Single(slots.Views);
        Assert.False(view.SetAngles);
        Assert.Contains("no drawn pose", camera.TargetNote);
    }
}
