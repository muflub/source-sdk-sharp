using SourceSharp.Ai.TF2.Knowledge;
using TF2.Server.Projectiles;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The lob the Demoman aims, flown as the game launches it: forward at the weapon's speed plus <see cref="TFProjectileDefs.PipeLaunchUp"/>
/// up along the view's up, falling at the game's 800 with the quadratic drag fitted to a stock pipe's flight
/// (<see cref="Drag"/>, within 7 u of stock's logged arc). It must come down within half a body of where it was aimed.
/// </summary>
public sealed class TFLobLaunchTests
{
    /// <summary>a = -k |v| v, k fitted to a stock grenade launcher pipe's logged flight (per unit).</summary>
    private const float Drag = TFKnowledge.GrenadeDrag;

    private const float HalfBody = 41f;

    private static readonly AiVector Eye = new(0f, 0f, 68f);

    /// <summary>The height the projectile has when its flat distance reaches the target's, flown from <see cref="Eye"/> at <paramref name="aim"/>.</summary>
    private static float HeightAtTarget(AiVector aim, AiVector target, float speed, float up)
    {
        AiVector forward = (aim - Eye).Normalised();
        AiVector zAxis = new(0f, 0f, 1f);
        AiVector upAxis = (zAxis - forward * forward.Dot(zAxis)).Normalised();
        AiVector v = forward * speed + upAxis * up;
        AiVector p = Eye;
        float d = target.Length2D;
        const float dt = 1f / 600f;
        for (int i = 0; i < 600 * 10; i++)
        {
            AiVector next = p + v * dt;
            v = v + new AiVector(0f, 0f, -800f) * dt - v * (Drag * v.Length * dt);
            if (next.Length2D >= d)
            {
                float t = (d - p.Length2D) / (next.Length2D - p.Length2D);
                return p.Z + (next.Z - p.Z) * t;
            }

            p = next;
        }

        throw new InvalidOperationException("the projectile never reached the target's distance");
    }

    private static float Miss(TFProjectile projectile, float speed, float distance, bool withLaunch, bool withDrag = false)
    {
        AiVector target = new(distance, 0f, 41f);
        float gravity = TFKnowledge.ProjectileGravity(projectile);
        float up = TFKnowledge.LaunchUp(projectile);
        AiVector? aim = withDrag ? AimLead.LobLaunchDragged(Eye, target, speed, gravity, up, Drag)
                        : withLaunch ? AimLead.LobLaunch(Eye, target, speed, gravity, up) : AimLead.Lob(Eye, target, speed, gravity);
        Assert.NotNull(aim);
        return MathF.Abs(HeightAtTarget(aim.Value, target, speed, up) - target.Z);
    }

    /// <summary>A pipe aimed by the launch's lob lands within half a body at 300, 600 and 900 u.</summary>
    [Theory]
    [InlineData(300f)]
    [InlineData(600f)]
    [InlineData(900f)]
    public void APipeAimedForTheLaunchLandsWithinHalfABody(float distance)
    {
        float miss = Miss(TFProjectile.Pipebomb, TFProjectileDefs.PipeSpeed, distance, withLaunch: true);
        Assert.True(miss <= HalfBody, $"missed by {miss:F1} u at {distance} u");
    }

    /// <summary>An uncharged sticky aimed by the launch's lob lands within half a body at 300 and 600 u.</summary>
    [Theory]
    [InlineData(300f)]
    [InlineData(600f)]
    public void AStickyAimedForTheLaunchLandsWithinHalfABody(float distance)
    {
        float miss = Miss(TFProjectile.PipebombRemote, TFProjectileDefs.StickyMinChargeSpeed, distance, withLaunch: true);
        Assert.True(miss <= HalfBody, $"missed by {miss:F1} u at {distance} u");
    }

    /// <summary>The control: the plain lob, blind to the up-kick, flies long by more than half a body at 600 u (the flight sees the kick).</summary>
    [Fact]
    public void APipeAimedWithoutTheLaunchFliesLong()
    {
        float miss = Miss(TFProjectile.Pipebomb, TFProjectileDefs.PipeSpeed, 600f, withLaunch: false);
        Assert.True(miss > HalfBody, $"missed by only {miss:F1} u");
    }

    /// <summary>
    /// A sticky charged from a sentry's stand (1,250 u at the 1,053 u/s the Demoman picks there; 1,500 u at 1,500 u/s) lands within half a
    /// body once the aim flies the drag (<see cref="AimLead.LobLaunchDragged"/>).
    /// </summary>
    [Theory]
    [InlineData(1250f, 1053f)]
    [InlineData(1500f, 1500f)]
    public void AChargedStickyAimedForTheDragLandsWithinHalfABody(float distance, float speed)
    {
        float miss = Miss(TFProjectile.PipebombRemote, speed, distance, withLaunch: true, withDrag: true);
        Assert.True(miss <= HalfBody, $"missed by {miss:F1} u at {distance} u");
    }

    /// <summary>The control: from the stand the drag-free aim falls short by more than half a body.</summary>
    [Fact]
    public void AChargedStickyAimedWithoutTheDragFallsShort()
    {
        float miss = Miss(TFProjectile.PipebombRemote, 1053f, 1250f, withLaunch: true);
        Assert.True(miss > HalfBody, $"missed by only {miss:F1} u");
    }

    /// <summary>With no drag the dragged lob is the launch's lob.</summary>
    [Fact]
    public void WithNoDragTheDraggedLobIsTheLaunchsLob()
    {
        AiVector target = new(700f, 0f, 41f);
        Assert.Equal(AimLead.LobLaunch(Eye, target, 1100f, 800f, 200f), AimLead.LobLaunchDragged(Eye, target, 1100f, 800f, 200f, 0f));
    }

    /// <summary>The pipe family's launch carries the game's up-kick; a rocket's none.</summary>
    [Fact]
    public void ThePipesLaunchKicksUpAndARocketsDoesNot()
    {
        Assert.Equal(TFProjectileDefs.PipeLaunchUp, TFKnowledge.LaunchUp(TFProjectile.Pipebomb));
        Assert.Equal(TFProjectileDefs.PipeLaunchUp, TFKnowledge.LaunchUp(TFProjectile.PipebombRemote));
        Assert.Equal(0f, TFKnowledge.LaunchUp(TFProjectile.Rocket));
    }

    /// <summary>
    /// The kick adds to the launch's speed: a tap has an arc onto a target 1,060 u off and 27 u below the eye, past the
    /// bare 900 u/s's reach (about 1,039 u) and inside the launch's 922 u/s (about 1,089 u).
    /// </summary>
    [Fact]
    public void TheKicksSpeedReachesPastTheBareSpeeds()
    {
        AiVector target = new(1060f, 0f, 41f);
        float gravity = TFKnowledge.ProjectileGravity(TFProjectile.PipebombRemote), up = TFKnowledge.LaunchUp(TFProjectile.PipebombRemote);
        Assert.Null(AimLead.Lob(Eye, target, TFProjectileDefs.StickyMinChargeSpeed, gravity));
        Assert.NotNull(AimLead.LobLaunch(Eye, target, TFProjectileDefs.StickyMinChargeSpeed, gravity, up));
    }

    /// <summary>With no up-kick the launch's lob is the plain lob.</summary>
    [Fact]
    public void WithNoKickTheLaunchsLobIsThePlainLob()
    {
        AiVector target = new(700f, 0f, 41f);
        Assert.Equal(AimLead.Lob(Eye, target, 1100f, 800f), AimLead.LobLaunch(Eye, target, 1100f, 800f, 0f));
    }
}
