//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>CParticleCollision</c> (particle_collision.cpp, with its base
/// <c>CBaseSimpleCollision</c>): a few planes found once, at Setup, by
/// stepping a particle's likely flight; each move is tested against them and
/// only a crossing pays for a real trace. A particle that lands slowly enough
/// on something floor-like stops; any other hit bounces it with a random
/// dampen. Replayed against <c>Fixtures/particlecollision-stock.tsv.gz</c>
/// (lane c5-particles), the leaf the fx_fleck / fx_sparks / fx_blood ports
/// move their particles with.
/// </summary>
public sealed class ParticleCollision
{
    /// <summary><c>MAX_COLLISION_PLANES</c>.</summary>
    public const int MaxPlanes = 6;

    private const float DiscreetSteps = 8.0f;
    private const float SimulationSeconds = 2.0f;
    private const float Epsilon = 0.01f;

    private readonly Vector[] _normals = new Vector[MaxPlanes];
    private readonly float[] _dists = new float[MaxPlanes];

    public float Gravity = 800.0f;
    public float CollisionDampen = 0.5f;
    public float AngularCollisionDampen = 0.25f;

    public ParticleCollision() => ClearActivePlanes();

    public int ActivePlanes { get; private set; }

    public (Vector Normal, float Dist) Plane(int i) => (_normals[i], _dists[i]);

    /// <summary><c>ClearActivePlanes</c>.</summary>
    public void ClearActivePlanes()
    {
        for (int i = 0; i < MaxPlanes; i++)
        {
            _dists[i] = -1.0f;
            _normals[i] = default;
        }
        ActivePlanes = 0;
    }

    /// <summary>
    /// <c>CParticleCollision::Setup</c>: the planes a particle launched from
    /// <paramref name="origin"/> at the average speed would meet in two
    /// seconds, along the six axes or, given a direction, along it and its
    /// right and left.
    /// </summary>
    public void Setup(Vector origin, Vector? direction, float angularSpread, float minSpeed, float maxSpeed,
                      float gravity, float dampen, IParticleWorldTrace world)
    {
        _ = angularSpread;
        Gravity = gravity;
        CollisionDampen = dampen;
        ActivePlanes = 0;

        float speedAvg = (minSpeed + maxSpeed) * 0.5f;
        if (direction is not { } dir)
        {
            TestForPlane(origin, new Vector(1, 0, 0), speedAvg, gravity, world);
            TestForPlane(origin, new Vector(-1, 0, 0), speedAvg, gravity, world);
            TestForPlane(origin, new Vector(0, 1, 0), speedAvg, gravity, world);
            TestForPlane(origin, new Vector(0, -1, 0), speedAvg, gravity, world);
            TestForPlane(origin, new Vector(0, 0, 1), speedAvg, gravity, world);
            TestForPlane(origin, new Vector(0, 0, -1), speedAvg, gravity, world);
            return;
        }

        Vector angles = VectorAngles(dir);
        MoveVec.AngleVectors(angles, out _, out Vector right, out _);
        TestForPlane(origin, dir, speedAvg, gravity, world);
        TestForPlane(origin, right, speedAvg, gravity, world);
        TestForPlane(origin, right * -1.0f, speedAvg, gravity, world);
    }

    /// <summary>
    /// <c>VectorAngles</c> (mathlib_base.cpp), as it compiles here: the
    /// float <c>atan2</c> overload, the product and quotient with
    /// <c>M_PI</c> in double.
    /// </summary>
    internal static Vector VectorAngles(Vector forward)
    {
        float yaw, pitch;
        if (forward.Y == 0 && forward.X == 0)
        {
            yaw = 0;
            pitch = forward.Z > 0 ? 270 : 90;
        }
        else
        {
            yaw = (float)(MathF.Atan2(forward.Y, forward.X) * 180 / Math.PI);
            if (yaw < 0)
                yaw += 360;
            float tmp = MathF.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
            pitch = (float)(MathF.Atan2(-forward.Z, tmp) * 180 / Math.PI);
            if (pitch < 0)
                pitch += 360;
        }
        return new Vector(pitch, yaw, 0);
    }

    /// <summary><c>TestForPlane</c>: eight steps of the flight; the first that hits adds its plane.</summary>
    private void TestForPlane(Vector start, Vector dir, float speed, float gravity, IParticleWorldTrace world)
    {
        Vector testStart = start;
        float stepTime = SimulationSeconds / DiscreetSteps;
        Vector stepIncr = dir * (speed * stepTime);
        float gravIncr = gravity * stepTime;

        for (int i = 1; i <= DiscreetSteps; i++)
        {
            Vector testEnd = testStart + stepIncr;
            testEnd.Z -= gravIncr * (0.5f * (stepTime * i) * (stepTime * i));
            ParticleTrace tr = world.TraceLine(testStart, testEnd);
            if (tr.Fraction != 1.0f)
            {
                ConsiderPlane(tr.Normal, tr.Dist);
                return;
            }
            testStart = testEnd;
        }
    }

    /// <summary><c>ConsiderPlane</c>: a plane not seen before, while there is room.</summary>
    private void ConsiderPlane(Vector normal, float dist)
    {
        for (int i = 0; i < ActivePlanes; i++)
        {
            if (_dists[i] != -1.0f && Equal(_normals[i], normal) && _dists[i] == dist)
                return;
        }
        if (ActivePlanes >= MaxPlanes)
            return;
        _dists[ActivePlanes] = dist;
        _normals[ActivePlanes] = normal;
        ActivePlanes++;
    }

    // Vector's operator==: exact, per component.
    private static bool Equal(Vector a, Vector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    private static float Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>
    /// <c>CBaseSimpleCollision::TraceLine</c> as MoveParticle calls it
    /// (coarse, the header's default): the first plane the segment crosses,
    /// or a miss. No real trace here; MoveParticle re-traces a crossing.
    /// </summary>
    private ParticleTrace TraceLine(Vector start, Vector end)
    {
        for (int i = 0; i < ActivePlanes; i++)
        {
            if (_dists[i] == -1.0f)
                continue;
            float dot1 = Dot(start, _normals[i]) - _dists[i];
            float dot2 = Dot(end, _normals[i]) - _dists[i];
            if (dot1 < -Epsilon)
                continue;
            if ((dot1 > Epsilon) == (dot2 > Epsilon))
                continue;

            float t = dot1 / (dot1 - dot2);
            var trace = new ParticleTrace
            {
                EndPos = start + (end - start) * t,
                Fraction = t - Epsilon,
                Normal = _normals[i],
                Dist = _dists[i],
            };
            return trace;
        }

        return new ParticleTrace { Fraction = 1.0f };
    }

    /// <summary>
    /// <c>MoveParticle</c>: gravity, then the step; a crossing re-traced for
    /// real; a floor-like hit at under 48 units/s stops the particle (and its
    /// roll), any other bounces it, reflected and dampened by a random factor
    /// around <see cref="CollisionDampen"/>, its roll reversed to a quarter.
    /// True when it bounced. <paramref name="trace"/> is stock's trace_t after
    /// the call: untouched (the caller's) when there are no planes or no
    /// crossing.
    /// </summary>
    public bool MoveParticle(ref Vector origin, ref Vector velocity, ref float rollDelta, bool hasRoll, float timeDelta,
                             IParticleWorldTrace world, IParticleRandom random, ref ParticleTrace trace)
    {
        if (Equal(velocity, default))
            return false;

        velocity.Z -= Gravity * timeDelta;
        Vector testPosition = origin + velocity * timeDelta;

        if (ActivePlanes > 0)
        {
            trace = TraceLine(origin, testPosition);
            if (trace.Fraction != 1.0f)
            {
                // __DEBUG_PARTICLE_COLLISION_RETEST is 1 off the Xbox: re-traced.
                trace = world.TraceLine(origin, testPosition);
                if (trace.Fraction != 1.0f)
                {
                    if (trace.Normal.Z >= 0.5f && MathF.Abs(velocity.Z) <= 48.0f)
                    {
                        origin = origin + velocity * ((trace.Fraction - Epsilon) * timeDelta);
                        velocity = default;
                        if (hasRoll)
                            rollDelta = 0.0f;
                        return false;
                    }

                    origin = origin + velocity * ((trace.Fraction - Epsilon) * timeDelta);
                    float proj = Dot(velocity, trace.Normal);
                    velocity = velocity + trace.Normal * (-proj * 2.0f);
                    velocity = velocity * random.RandomFloat(CollisionDampen - 0.1f, CollisionDampen + 0.1f);
                    if (hasRoll)
                        rollDelta *= -0.25f;
                    return true;
                }
            }
        }

        origin = testPosition;
        return false;
    }
}
