using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

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

    private sealed class Eyes(params IObserverEyeAngles[] all) : IHooks<IObserverEyeAngles>
    {
        public IReadOnlyList<IObserverEyeAngles> All => all;

        public void Each(string what, Action<IObserverEyeAngles> call)
        {
            foreach (IObserverEyeAngles source in all)
                call(source);
        }
    }

    /// <summary>A mod that knows where player <see cref="Watched"/> looks.</summary>
    private sealed class Aim(QAngle angles) : IObserverEyeAngles
    {
        public bool TryEyeAngles(int playerIndex, out QAngle eyes)
        {
            eyes = angles;
            return playerIndex == Watched;
        }
    }

    private static (ObserverCamera Camera, Slots Slots, ClientSsPlayer Me) Build() => Build(new Wishes());

    private static (ObserverCamera Camera, Slots Slots, ClientSsPlayer Me) Build(Wishes wishes, Gated? gated = null,
                                                                                 IObserverEyeAngles? eyes = null,
                                                                                 Freezes? freeze = null, Func<float>? now = null)
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", Local);
        engine.ReturnsEachCall("Clock.Now", () => now?.Invoke() ?? 10f);

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

        var camera = new ObserverCamera(slots, directory, engine, new ViewReadout(), engine, DeclaredConVars.Holding<ClForceCameraConVar>("1"), wishes,
                                        eyes is null ? new Eyes() : new Eyes(eyes),
                                        new MovementRulesResolver(new ServiceContainer()), new MidRandom(), freeze ?? new Freezes(),
                                        DeclaredConVars.Holding<ClSpecFreezeTimeConVar>("4.0"),
                                        DeclaredConVars.Holding<ClSpecFreezeTravelTimeConVar>("0.4"),
                                        DeclaredConVars.Holding<SpecFreezeDistanceMinConVar>("96"),
                                        DeclaredConVars.Holding<SpecFreezeDistanceMaxConVar>("200"));
        return (camera, slots, me);
    }

    /// <summary>The middle of every range: the freeze distance 148, the height offset -5.</summary>
    private sealed class MidRandom : IUniformRandom
    {
        public float RandomFloat(float min, float max) => (min + max) / 2;
        public int RandomInt(int min, int max) => (min + max) / 2;
    }

    private sealed class Freezes : IViewFreezeFrame
    {
        public readonly List<(float FreezeTime, float CurTime)> Asked = [];
        public void ViewFreezeFrame(float freezeTime, float curTime) => Asked.Add((freezeTime, curTime));
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
    public void TheFreezeCamFliesToFrameTheKillerThenFreezesThePicture()
    {
        // CalcFreezeCamView: from where the view was, along a spline, to a
        // spot in front of the target (148 units off at -5 above its eyes,
        // the random ranges' middles); at spec_freeze_traveltime the picture
        // freezes for spec_freeze_time.
        float time = 10f;
        var freezes = new Freezes();
        var (camera, slots, me) = Build(new Wishes(), freeze: freezes, now: () => time);
        me.State.ObserverMode = (int)ObserverMode.FreezeCam;
        me.State.ObserverTarget = 0x0001_0000u | Watched;

        camera.Pass();
        Vector start = slots.Views[0].Origin;
        time = 10.2f;
        camera.Pass();
        Vector halfway = slots.Views[1].Origin;
        time = 10.41f;
        camera.Pass();
        Vector arrived = slots.Views[2].Origin;

        Assert.NotEqual(start, arrived);
        Assert.InRange((halfway - start).Length, 0.01f, (arrived - start).Length - 0.01f);
        Assert.Equal(64f - 5f, arrived.Z, 2);                          // the watched eye (0 + 64), -5
        Assert.Equal(148f, (new Vector(100, 200, 0) - new Vector(arrived.X, arrived.Y, 0)).Length, 1);
        Assert.Equal([(4.0f, 10.41f)], freezes.Asked);
        Assert.Equal(10.41f, camera.FreezeStartedAt);
    }

    [Fact]
    public void TheFreezeCamLooksAtTheTarget()
    {
        float time = 10f;
        var (camera, slots, me) = Build(new Wishes(), now: () => time);
        me.State.ObserverMode = (int)ObserverMode.FreezeCam;
        me.State.ObserverTarget = 0x0001_0000u | Watched;
        camera.Pass();
        time = 11f;
        camera.Pass();

        var view = slots.Views[^1];
        Vector forward = MoveVec.AngleForward(view.Angles.AsVector());
        Vector toChest = (new Vector(100, 200, 64 - 36) - view.Origin).Normalized;   // eye height 64, half the 72 hull
        Assert.True(forward.X * toChest.X + forward.Y * toChest.Y + forward.Z * toChest.Z > 0.999f, $"looks {forward}, the chest is {toChest}");
    }

    [Fact]
    public void LeavingTheFreezeCamEndsTheFrozenPicture()
    {
        float time = 10f;
        var freezes = new Freezes();
        var (camera, _, me) = Build(new Wishes(), freeze: freezes, now: () => time);
        me.State.ObserverMode = (int)ObserverMode.FreezeCam;
        me.State.ObserverTarget = 0x0001_0000u | Watched;
        camera.Pass();
        time = 10.5f;
        camera.Pass();

        me.State.ObserverMode = (int)ObserverMode.Chase;
        time = 14f;
        camera.Pass();

        Assert.Equal([(4.0f, 10.5f), (0f, 14f)], freezes.Asked);
        Assert.Null(camera.FreezeStartedAt);
    }

    [Fact]
    public void InEyeLooksWhereTheModSaysTheWatchedPlayerLooks()
    {
        // C_TFPlayer::EyeAngles: the networked m_angEyeAngles, pitch and all,
        // not the body's facing (yaw 45, pitch 0 in the drawn pose).
        var (camera, slots, me) = Build(new Wishes(), eyes: new Aim(new QAngle(-30, 120, 0)));
        me.State.ObserverMode = (int)ObserverMode.InEye;
        me.State.ObserverTarget = 0x0001_0000u | Watched;

        camera.Pass();

        Assert.Equal(new QAngle(-30, 120, 0), Assert.Single(slots.Views).Angles);
    }

    [Fact]
    public void WithoutAModsWordInEyeTakesTheBodysFacing()
    {
        var (camera, slots, me) = Build();
        me.State.ObserverMode = (int)ObserverMode.InEye;
        me.State.ObserverTarget = 0x0001_0000u | Watched;

        camera.Pass();

        Assert.Equal(new QAngle(0, 45, 0), Assert.Single(slots.Views).Angles);
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
