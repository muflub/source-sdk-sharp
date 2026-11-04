//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Detail;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.TempEnts;

using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Client;

/// <summary>
/// <c>CTETFBlood</c>, played — <c>TFBloodSprayCallback</c>
/// (<c>tf_fx_blood.cpp:29</c>) and <c>C_TETFBlood::PostDataUpdate</c>, which
/// is the whole of that class: the callback with its own three fields.
///
/// <para>
/// THE IMPACT AND ITS SPRAY ARE TWO DISPATCHES, chosen by five arms:
/// </para>
/// <code>
/// if ( !bUnderwater &amp;&amp; TFGameRules() &amp;&amp; IsBirthday() &amp;&amp; RandomFloat(0,1) &lt; 0.2 )  "bday_blood"
/// else if ( TFGameRules() &amp;&amp; bPyroVision )                                       "pyrovision_blood"
/// else if ( UTIL_IsLowViolence() )   lowV_water_blood_impact_red_01 | lowV_blood_impact_red_01
/// else                               water_blood_impact_red_01 | blood_impact_red_01
/// </code>
/// <para>
/// then, unless underwater, a spray pushed off the view — its normal nudged
/// sideways when it runs along the view direction, its roll made and its
/// near/far name taken from the distance to <c>MainViewOrigin()</c>.
/// </para>
///
/// <para>
/// <b>THE RNG IS CONSUMED IN STOCK'S ORDER</b>, because the arms short-circuit:
/// the birthday roll is spent only when the birthday arm is reached, the push
/// roll only when the normal runs near the view, and the close-range side roll
/// only under that and within 512 units. A port that rolled unconditionally
/// would desync from stock's <c>random</c> stream one effect earlier than any
/// test of the numbers could see.
/// </para>
///
/// <para>
/// <b>THE ENTITY DROPS OUT OF THE DISPATCH, AND THAT IS THE PORT'S SHAPE, NOT
/// AN OMISSION.</b> Stock's <c>DispatchParticleEffect(name, origin, angles,
/// pPlayer)</c> ends at the shared helper our build carries
/// (<c>particle_manifest.cpp</c>'s moved <c>DispatchParticleEffect</c>), which
/// passes <c>Start = Origin</c> and ignores the entity — so the effect this
/// build plays is the world route of <c>ParticleEffect</c>: control point 0
/// and 1 at the origin, orientation from the angles, no owner. The name is
/// handed to <see cref="IClientParticleOneShots.Start"/> directly, which is
/// that route's own call without the index indirection stock's
/// <c>GetParticleSystemIndex</c> exists for and this port has no need of.
/// </para>
///
/// <para>
/// <c>TFGameRules() &amp;&amp;</c>, on the pyro arm, gates on a rules object
/// this port's client has no mirror of; the gate's only false case is the main
/// menu, and a temp entity cannot arrive there. Both arms are read ungated.
/// </para>
/// </summary>
[ClientTempEnt("CTETFBlood")]
public sealed class ClientTeTFBlood(IClientParticleOneShots oneShots, IClientStandalones standalones,
                                    SourceSharp.Client.Effects.IClientLowViolence lowViolence,
                                    SourceSharp.Client.LocalPlayerVisionFlagsConVar visionFlags, TFHolidayConVars holidays,
                                    IUniformRandom random, IFxRender fx, TimeProvider time) : ClientTempEnt<TeTFBlood>
{
    /// <summary>TF_VISION_FILTER_PYRO (<c>shareddefs.h:984</c>).</summary>
    private const int VisionFilterPyro = 1 << 0;

    public override void Play(TeTFBlood te)
    {
        Vector origin = te.Origin;
        Vector normal = te.Normal;

        // VectorAngles( -vecNormal, vecAngles ): the effect faces back INTO
        // the wall the hit came from.
        Vector angles = DetailMath.VectorAngles(new Vector(-normal.X, -normal.Y, -normal.Z));

        // determine if the bleeding player is underwater: the entity at the
        // index is asked its water level, and only a TF player's answer counts
        // (stock's dynamic_cast to C_TFPlayer).
        bool underwater = standalones.GetByIndex(te.EntIndex) is ClientSsPlayer player
                          && player.State.WaterLevel == GameMovement.WlEyes;

        // Use birthday fun if the local player has an item that allows them to
        // see it (Pyro Goggles). IsLocalPlayerUsingVisionFilterFlags has no
        // body in the SDK tree; the read is the cvar bit PyroVision uses.
        bool pyroVision = VisionFilter();

        if (!underwater && TFHolidays.IsBirthday(holidays.Switches, time.GetLocalNow().DateTime) && random.RandomFloat(0f, 1f) < 0.2f)
        {
            Dispatch("bday_blood", origin, angles);
        }
        else if (pyroVision)
        {
            Dispatch("pyrovision_blood", origin, angles);
        }
        else if (lowViolence.IsLowViolence)
        {
            Dispatch(underwater ? "lowV_water_blood_impact_red_01" : "lowV_blood_impact_red_01",
                     origin, angles);
        }
        else
        {
            Dispatch(underwater ? "water_blood_impact_red_01" : "blood_impact_red_01",
                     origin, angles);
        }

        // if underwater, don't add additional spray
        if (underwater)
            return;

        // Now throw out a spray away from the view
        float distance = MoveVec.Length(origin - fx.MainViewOrigin());
        float lodDistance = 0.25f * (distance / 512f);

        // The basis of the plane the normal faces: the cross products build the
        // right and up the push below displaces along, and stock's up arm for
        // the straight-up normal is its own else.
        Vector right, up;
        if (normal.X != 0f || normal.Y != 0f || normal.Z != 1f)
        {
            right = normal.Cross(new Vector(0, 0, 1));
            up = right.Cross(normal);
        }
        else
        {
            right = new Vector(0, 0, 1);
            up = right.Cross(normal);
        }

        // If the normal's too close to being along the view, push it out
        MoveVec.AngleVectors(fx.ViewAngles().AsVector(),
                             out Vector forward, out Vector viewRight, out _);

        float dot = normal.Dot(forward);
        if (MathF.Abs(dot) > 0.5f)
        {
            float push = random.RandomFloat(0.5f, 1.5f) + lodDistance;
            float rightDot = normal.Dot(viewRight);

            // If we're up close, randomly move it around. If we're at a
            // distance, always push it to the side.
            if ((distance >= 512f && rightDot > 0) || (distance < 512f && random.RandomFloat(0f, 1f) > 0.5f))
            {
                normal += viewRight * push;
            }
            else
            {
                normal -= viewRight * push;
            }
        }

        Vector sprayAngles = DetailMath.VectorAngles(normal);

        if (distance < 400f)
        {
            Dispatch(lowViolence.IsLowViolence ? "lowV_blood_spray_red_01" : "blood_spray_red_01",
                     origin, sprayAngles);
        }
        else
        {
            Dispatch(lowViolence.IsLowViolence ? "lowV_blood_spray_red_01_far" : "blood_spray_red_01_far",
                     origin, sprayAngles);
        }
    }

    /// <summary>
    /// <c>DispatchParticleEffect(name, origin, angles, pPlayer)</c> as this
    /// build's helper routes it: a one-shot at the origin, control points 0
    /// and 1 both at it, oriented by the angles. See the class note.
    /// </summary>
    private void Dispatch(string name, Vector origin, Vector angles)
    {
        MoveVec.AngleVectors(in angles, out Vector forward, out Vector right, out Vector up);

        Span<ParticleControlPoint> points = stackalloc ParticleControlPoint[2];
        points[0] = new ParticleControlPoint(0, origin);
        points[1] = new ParticleControlPoint(1, origin);

        oneShots.Start(name, origin, points, forward, right, up);
    }

    /// <summary><c>localplayer_visionflags</c> &amp; TF_VISION_FILTER_PYRO.</summary>
    private bool VisionFilter() => (visionFlags.Value & VisionFilterPyro) != 0;
}
