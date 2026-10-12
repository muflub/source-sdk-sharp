using SourceSharp.Abi;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ObserverView"/> (plan_client_novalve C7 item 5): stock's
/// CalcObserverView and the views it chooses between (c_baseplayer.cpp), each
/// fact naming the rule it holds.
/// </summary>
public class ObserverViewTests
{
    private sealed class World(float fraction = 1f) : IObserverWorld
    {
        public readonly List<(Vector Start, Vector End, ObserverTraceFilter Filter, int Pass)> Traces = [];

        public ObserverTrace TraceHull(Vector start, Vector end, ObserverTraceFilter filter, int passIndex)
        {
            Traces.Add((start, end, filter, passIndex));
            return new ObserverTrace(fraction, start + (end - start) * fraction);
        }
    }

    private static ObserverTarget Target(bool alive = true, bool ducking = false, bool visible = true)
        => new(7, new Vector(100, 0, 0), new QAngle(10, 90, 0), new Vector(100, 0, 64), visible, true, alive, ducking,
               new Vector(0, 0, 64));

    private static ObserverInputs Inputs(ObserverMode mode, ObserverTarget? target, float frameTime = 0.1f)
        => new(mode, target, new Vector(0, 0, 64), new QAngle(0, 0, 0), new QAngle(0, 180, 0), new Vector(0, 0, 64),
               new QAngle(1, 2, 0), frameTime, 10f, 9f, true, null);

    private static void Near(Vector want, Vector got)
    {
        Assert.True((want - got).Length < 0.01f, $"{want} against {got}");
    }

    [Fact]
    public void NoObserverModeIsThePlayersOwnView()
        => Assert.Null(new ObserverView().Calc(Inputs(ObserverMode.None, Target()), new World()));

    [Fact]
    public void InEyeLooksFromTheTargetsBodyWithTheObserversOffsetAndPunch()
    {
        // CalcInEyeCamView: target->GetAbsOrigin() + GetViewOffset() (the
        // observer's, "hack hack"), target->EyeAngles() + GetPunchAngle(), and
        // engine->SetViewAngles.
        var v = new ObserverView { ChaseDistance = 50 }.Calc(Inputs(ObserverMode.InEye, Target()), new World())!.Value;
        Near(new Vector(100, 0, 64), v.Origin);
        Assert.Equal(new QAngle(11, 92, 0), v.Angles);
        Assert.True(v.SetViewAngles);
    }

    [Fact]
    public void InEyeOnADeadTargetIsTheChaseView()
    {
        var view = new ObserverView();
        var v = view.Calc(Inputs(ObserverMode.InEye, Target(alive: false)), new World())!.Value;
        Assert.False(v.SetViewAngles);
        Assert.NotEqual(new Vector(100, 0, 64), v.Origin);
    }

    [Fact]
    public void TheChaseCameraSitsBehindTheTargetsHeadAlongTheViewAngles()
    {
        // CalcChaseCamView: origin = cam origin + VEC_VIEW; distance grows by
        // frametime*48 and is clamped to [16, 96]; the eye goes back along the
        // engine's view angles (yaw 180 here, so +x).
        var view = new ObserverView();
        var world = new World();
        var v = view.Calc(Inputs(ObserverMode.Chase, Target()), world)!.Value;
        Assert.Equal(16f, view.ChaseDistance);
        Near(new Vector(116, 0, 64), v.Origin);
        Assert.Equal(new QAngle(0, 180, 0), v.Angles);
        Assert.Equal(ObserverTraceFilter.NoPlayersPassing, world.Traces[0].Filter);
        Assert.Equal(7, world.Traces[0].Pass);
    }

    [Fact]
    public void TheChaseDistanceGrowsToItsCap()
    {
        var view = new ObserverView();
        for (int i = 0; i < 100; i++)
            view.Calc(Inputs(ObserverMode.Chase, Target()), new World());
        Assert.Equal(ObserverView.ChaseCamDistanceMax, view.ChaseDistance);
    }

    [Fact]
    public void ADuckingTargetsChaseOriginIsItsDuckViewHeight()
    {
        var world = new World();
        new ObserverView().Calc(Inputs(ObserverMode.Chase, Target(ducking: true)), world);
        Near(new Vector(100, 0, 28), world.Traces[0].Start);
    }

    [Fact]
    public void AWallStopsTheChaseCameraAndRemeasuresFromTheLastView()
    {
        var view = new ObserverView { LastOrigin = new Vector(100, 0, 34) };
        var v = view.Calc(Inputs(ObserverMode.Chase, Target()), new World(0.5f))!.Value;
        Near(new Vector(108, 0, 64), v.Origin);
        Assert.Equal(30f, view.ChaseDistance, 3);
    }

    [Fact]
    public void AnInvisibleTargetIsAFixedCamera()
    {
        // CalcChaseCamView: no model -> CalcRoamingView, the target's own eye.
        var v = new ObserverView().Calc(Inputs(ObserverMode.Chase, Target(visible: false)), new World())!.Value;
        Near(new Vector(100, 0, 64), v.Origin);
        Assert.Equal(new QAngle(10, 90, 0), v.Angles);
    }

    [Fact]
    public void RoamingWithNoTargetIsThePlayersOwnEye()
    {
        var v = new ObserverView { ChaseDistance = 40 }.Calc(Inputs(ObserverMode.Roaming, null), new World())!.Value;
        Near(new Vector(0, 0, 64), v.Origin);
    }

    [Fact]
    public void TheDeathCameraTurnsTowardsTheKillerOverThreeSeconds()
    {
        // CalcDeathCamView: (curtime - deathtime) / DEATH_ANIMATION_TIME, here
        // 1/3 of the way from the player's angles (yaw 0) to the killer's
        // bearing (yaw 0 too: straight down +x) -- and backs off at least 32.
        var view = new ObserverView();
        var world = new World();
        var v = view.Calc(Inputs(ObserverMode.DeathCam, Target()), world)!.Value;
        Assert.Equal(32f, view.ChaseDistance);
        Assert.Equal(ObserverTraceFilter.SimplePassingSelf, world.Traces[0].Filter);
        Assert.True(v.Origin.X < 0, $"backed away from the killer: {v.Origin}");
    }

    [Fact]
    public void InterpolateAnglesIsTheEndpointsAtZeroAndOne()
    {
        var a = new QAngle(0, 10, 0);
        var b = new QAngle(0, 70, 0);
        Assert.Equal(10f, ObserverView.InterpolateAngles(a, b, 0f).Yaw, 3);
        Assert.Equal(70f, ObserverView.InterpolateAngles(a, b, 1f).Yaw, 3);
        Assert.Equal(40f, ObserverView.InterpolateAngles(a, b, 0.5f).Yaw, 3);
    }
}
