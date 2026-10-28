//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.TempEnts;

/// <summary>The four knobs a bullet impact reads (ConVars.cs declares them).</summary>
/// <param name="DrawImpactDebris"><c>fx_drawimpactdebris</c>: FX_DebrisFlecks draws.</param>
/// <param name="DrawImpactDust"><c>fx_drawimpactdust</c>: FX_DustImpact draws.</param>
/// <param name="DrawFlecks"><c>r_drawflecks</c>: an impact's flecks draw.</param>
/// <param name="NewEffects"><c>cl_new_impact_effects</c>: the particle-system table rather than the chain.</param>
public sealed record BulletImpactSettings(Func<bool> DrawImpactDebris, Func<bool> DrawImpactDust,
                                          Func<bool> DrawFlecks, Func<bool> NewEffects);

/// <summary>
/// The "Impact" client effect: one bullet's mark, on whatever it landed on.
/// Stock's <c>ImpactCallback</c> (hl2/fx_hl2_impacts.cpp) and the fx_impact.cpp
/// chain under it (<c>ParseImpactData</c>, <c>Impact</c>'s trace and decal,
/// <c>PerformCustomEffects</c>, <c>PlayImpactSound</c>), with
/// c_impact_effects.cpp's <c>GetColorForSurface</c>, <c>FX_DebrisFlecks</c> and
/// <c>FX_DustImpact</c> entry points. It was shell_impact_effects.cpp's
/// <c>SourceSharp_ImpactCallback</c> until lane M1-C, and it is that function's
/// order, which is stock's with each step spelt the way this hierarchy answers:
///
/// <list type="number">
/// <item>the ragdoll sweep (a refusal on this client, printed once);</item>
/// <item>the trace, clipped to the entity the server named first and the world
/// after (glass stops being solid to MASK_SHOT the moment its pane goes, so a
/// world trace first would put the impact on the wall behind);</item>
/// <item>the decal: a static prop, a studio model, or everything else, the
/// switch <c>C_BaseEntity::AddDecal</c> makes on the model type;</item>
/// <item>the custom effect, only when the trace hit and not on sky, nodraw,
/// hint or skip;</item>
/// <item>the sound, whether or not anything else happened.</item>
/// </list>
///
/// <para>
/// What it asks the engine is <see cref="IClientImpactEngine"/> (the trace,
/// the two surface-lighting reads, the new-style particle, the effect ledger)
/// and the decal and spark slots; which effect a surface gets is
/// <see cref="ImpactChoicePolicy"/>'s.
/// </para>
/// </summary>
public sealed class BulletImpactEffect(IClientImpactEngine engine, IEngineWorldQuery world, IEngineEffects effects,
                                       IClientDecals decals, IClientImpactSprays sprays, IClientImpactSparks sparks,
                                       IParticleThrottle throttle, IClientParticleOneShots oneShots,
                                       IFwdPhysicsSurfaceProps surfaces, Sounds.IClientEntitySounds sounds,
                                       BulletImpactSettings settings, IDecalSettings decalSettings, ILog log)
    : IClientEffectHandler
{
    /// <summary>bspflags.h: <c>SURF_SKY | SURF_NODRAW | SURF_HINT | SURF_SKIP</c>, the surfaces that take no effect.</summary>
    public const int NoEffectSurfaces = 0x0004 | 0x0080 | 0x0100 | 0x0200;

    /// <summary>How far past the impact the trace runs: stock's <c>flLength + 8.0f</c>.</summary>
    public const float TraceOvershoot = 8.0f;

    /// <summary>The scale this callback passes (ImpactJeep and ImpactGauss pass 2).</summary>
    public const int Scale = 1;

    private bool _refusalSaid;

    public string EffectName => "Impact";

    /// <summary>Impacts handled since load.</summary>
    public int Impacts { get; private set; }

    /// <summary>Flecks and dust particles the impacts started.</summary>
    public int Flecks { get; private set; }

    /// <inheritdoc cref="Flecks"/>
    public int Dust { get; private set; }

    public void Dispatch(in EffectData data)
    {
        Impacts++;

        // ParseImpactData: the unpacking, and the game material behind the
        // surfaceprop the server traced.
        Vector origin = data.Origin;
        Vector start = data.Start;
        int surfaceProp = (short)data.SurfaceProp;
        int damageType = data.DamageType;
        int hitbox = data.HitBox;
        int material = (int)world.SurfacePropGameMaterial(surfaceProp);

        Vector shotDir = FxMath.Sub(origin, start);
        float length = FxMath.Normalize(ref shotDir);
        Vector traceExt = FxMath.MA(start, length + TraceOvershoot, shotDir);

        RefuseRagdollSweep();

        // The entity the server named, clipped first; the world when the clip
        // missed or there is no such entity on this client.
        ShotTrace? clip = engine.ClipToEntity(start, traceExt, data.EntIndex);
        int entity = clip is null ? -1 : data.EntIndex;
        ShotTrace tr = clip is { Hit: true } hit ? hit : engine.TraceWorld(start, traceExt);

        ImpactChoice choice = ImpactChoicePolicy.Choose(material, Scale, 0, settings.DrawFlecks(),
                                                        settings.NewEffects(), damageType);

        int decal = DecalIndex(choice.DecalName, material);
        if (decal != -1)
            Decal(decal, entity, hitbox, start, traceExt, tr);

        if (tr.Hit)
            Perform(choice, origin, start, tr, shotDir, material);

        PlayImpactSound(tr, origin, surfaceProp);
    }

    /// <summary>
    /// <c>GetDecalIndexForName( TranslateDecalForGameMaterial( name, material ) )</c>:
    /// the material picks the group the decal name becomes; −1 for none.
    /// </summary>
    private int DecalIndex(string name, int material)
    {
        if (string.IsNullOrEmpty(name))
            return -1;

        // The material crosses as stock's unsigned char.
        string translated = decals.TranslateForGameMaterial(name, material & 0xFF) ?? name;
        return translated.Length == 0 ? -1 : decals.IndexForName(translated);
    }

    /// <summary>
    /// <c>C_BaseEntity::AddDecal</c>'s switch, asked of this hierarchy: a
    /// non-zero hitbox on entity 0 is static prop hitbox − 1; a studio model
    /// takes the ray; the world, a brush model and the rest take the point the
    /// trace found. The last arm is <c>r_decals</c>'s, as every DecalShoot this
    /// client makes is; the studio arm is <c>r_drawmodeldecals</c>'s
    /// (<see cref="ClientEntityDecal.AddStudioDecal"/>).
    /// </summary>
    private void Decal(int decal, int entity, int hitbox, Vector start, Vector traceExt, in ShotTrace tr)
    {
        if (entity == 0 && hitbox != 0)
            effects.ClientDecalStaticProp(start, traceExt, hitbox - 1, decal);
        else if (world.ClientEntityModelType(entity) == ModelType.Studio)
            ClientEntityDecal.AddStudioDecal(effects, decalSettings, entity, start, traceExt, decal);
        else if (decalSettings.Decals)
            effects.ClientDecalShoot(decal, entity, tr.EndPos, null, 0);
    }

    /// <summary>
    /// <c>PerformCustomEffects</c> for a trace that hit: the choice's effect,
    /// unless the surface is sky, nodraw, hint or skip.
    /// </summary>
    private void Perform(in ImpactChoice choice, Vector origin, Vector start, in ShotTrace tr, Vector shotDir,
                         int material)
    {
        if ((tr.SurfaceFlags & NoEffectSurfaces) != 0)
            return;

        switch (choice.Action)
        {
            case ImpactAction.DebrisFlecks:
                if (!settings.DrawImpactDebris())
                    break;
                {
                    int made = sprays.Debris(tr.EndPos, tr.Normal, ColorForSurface(start, tr), (sbyte)material,
                                             choice.Scale, choice.NoFlecks, throttle.ThrottleScaling());
                    if (made > 0)
                    {
                        Flecks += made;
                        engine.Ledger(made, 0);
                    }
                }
                break;

            case ImpactAction.Dust:
                if (!settings.DrawImpactDust())
                    break;
                {
                    int made = sprays.Dust(origin, tr.Normal, ColorForSurface(start, tr), choice.Scale,
                                           throttle.ThrottleScaling());
                    if (made > 0)
                    {
                        Dust += made;
                        engine.Ledger(0, made);
                    }
                }
                break;

            case ImpactAction.Antlion:
                // FX_AntlionImpact returns before it draws when the hit entity's
                // bounds are unknown, and they always are on this ABI.
                break;

            case ImpactAction.MetalSpark:
                sparks.ImpactMetalSpark(origin, shotDir, tr.Normal, choice.Scale, throttle.ThrottleScaling());
                break;

            case ImpactAction.Sparks:
                effects.ClientSparks(FxMath.MA(origin, 1.0f, tr.Normal), 1, 1, null);
                break;

            case ImpactAction.Particle:
                DispatchParticleEffect(choice.ParticleName, origin, shotDir);
                break;

            case ImpactAction.NewParticle:
                NewParticleImpact(choice.ParticleName, origin, start, tr, shotDir, choice.Scale);
                break;
        }
    }

    /// <summary>
    /// <c>DispatchParticleEffect( name, origin, angles )</c> with the angles of
    /// the shot reversed: the ParticleEffect client effect's world-point arm,
    /// control points 0 and 1 at the origin.
    /// </summary>
    private void DispatchParticleEffect(string name, Vector origin, Vector shotDir)
    {
        Vector angles = Detail.DetailMath.VectorAngles(FxMath.Mul(shotDir, -1.0f));
        MoveVec.AngleVectors(angles, out Vector forward, out Vector right, out Vector up);
        Span<ParticleControlPoint> points = [new ParticleControlPoint(0, origin), new ParticleControlPoint(1, origin)];
        oneShots.Start(name, origin, points, forward, right, up);
    }

    /// <summary>
    /// <c>PerformNewCustomEffects</c>: the named system with control point 0
    /// along the surface normal, 1 along the reflected shot and 2 back along
    /// it, all at the impact point; 3 the scale and 4 the surface colour.
    /// </summary>
    private void NewParticleImpact(string name, Vector origin, Vector start, in ShotTrace tr, Vector shotDir,
                                   int scale)
    {
        float dot = FxMath.Dot(shotDir, tr.Normal);
        Vector reflect = FxMath.MA(shotDir, -2.0f * dot, tr.Normal);
        Vector backward = FxMath.Mul(shotDir, -1.0f);
        Vector point = tr.Fraction != 1.0f ? tr.EndPos : origin;

        Span<ImpactControlPoint> points = [Oriented(point, tr.Normal), Oriented(point, reflect), Oriented(point, backward)];
        engine.ImpactParticle(name, points, tr.EntityIndex, scale, ColorForSurface(start, tr));
    }

    /// <summary>SetImpactControlPoint's orientation: <c>VectorVectors</c> with its Y negated.</summary>
    private static ImpactControlPoint Oriented(Vector point, Vector forward)
    {
        FxMath.VectorVectors(forward, out Vector y, out Vector z);
        return new ImpactControlPoint(point, forward, FxMath.Mul(y, -1.0f), z);
    }

    /// <summary>
    /// <c>GetColorForSurface</c>: the light at the surface the trace found,
    /// gamma corrected (<c>pow( diffuse, 1/2.2 ) * base</c>). The world asks
    /// the world, a static prop its own lighting; another entity has no
    /// surface on this hierarchy (stock's entity arm reads its collideable's
    /// model, which no entity here answers) and is white, as stock's
    /// no-entity arm is.
    /// </summary>
    private Vector ColorForSurface(Vector start, in ShotTrace tr)
    {
        Vector diffuse, baseColor;

        if (tr.HitWorld)
        {
            if (tr.Hitbox == 0)
            {
                Vector end = FxMath.Add(start, FxMath.Mul(FxMath.Sub(tr.EndPos, start), 1.1f));
                (diffuse, baseColor) = engine.WorldLighting(start, end);
            }
            else
            {
                (diffuse, baseColor) = engine.StaticPropLighting(start, tr.EndPos, tr.Normal, tr.Hitbox - 1);
            }
        }
        else
        {
            log.Msg("Couldn't find surface in GetColorForSurface()\n");
            return new Vector(255f, 255f, 255f);
        }

        const float Gamma = 1.0f / 2.2f;
        return new Vector(MathF.Pow(diffuse.X, Gamma) * baseColor.X,
                          MathF.Pow(diffuse.Y, Gamma) * baseColor.Y,
                          MathF.Pow(diffuse.Z, Gamma) * baseColor.Z);
    }

    /// <summary>
    /// <c>PlayImpactSound</c>: the trace's surface when the server named none,
    /// the point the trace stopped at when it stopped, and the surface's
    /// bullet-impact sound from the world.
    /// </summary>
    private void PlayImpactSound(in ShotTrace tr, Vector serverOrigin, int serverSurfaceProp)
    {
        // The identity half of stock's test (the client trace hit a different
        // entity than the server's) needs the server's entity on the trace's
        // base, which this hierarchy has no way to compare; what is answerable
        // is the server naming no surfaceprop.
        if (tr.Hit && serverSurfaceProp == 0)
            serverSurfaceProp = tr.SurfaceProps;

        Vector origin = tr.Fraction < 1.0f ? tr.EndPos : serverOrigin;

        if (surfaces.BulletImpactSound(serverSurfaceProp) is { } name)
            sounds.EmitFrom(EntityHandle.None, name, origin);
    }

    /// <summary>
    /// <c>FX_AffectRagdolls</c>, a declared refusal: the ray walk has no subject
    /// on this client (no client ragdoll joins the spatial partition), so it is
    /// said once rather than run and found empty.
    /// </summary>
    private void RefuseRagdollSweep()
    {
        if (_refusalSaid)
            return;

        _refusalSaid = true;
        log.Warning("SourceSharp: the bullet's push on a ragdoll along the shot ray (FX_AffectRagdolls) is REFUSED "
                  + "in this build (plan_entity.md §7). The client's ragdolls are managed and join no spatial "
                  + "partition, so the ray walk has no subject; the sphere twin is ClientTeExplosion.AffectRagdolls.\n");
    }
}
