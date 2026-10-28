//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A blast that throws physics objects around: env_physexplosion.
///
/// Not the same thing as an explosion. There is no fireball, no sound and no
/// radius damage unless it is asked for — this is the entity a mapper uses to
/// blow a stack of crates over on cue, and the one that does the visible half
/// is env_explosion beside it.
///
/// The falloff is Valve's and is worth reading twice: damage falls off with
/// DISTANCE at a fixed rate (1/2.5 per unit), not as a fraction of the radius.
/// So the radius decides who is caught and the magnitude decides how far the
/// falloff reaches, and a large radius with a small magnitude affects distant
/// things by the minimum of one point rather than by a scaled amount.
/// </summary>
[ScriptedEntity(EntityNames.EnvPhysexplosion)]
public sealed class PhysExplosion : PhysicsEmitter
{
    public PhysExplosion(IEngineWorldQuery world, IEngineDamage damage, IEngineNetworking net,
                         IEnginePlayerMovement movement, IEngineHud hud, IEngineHost host)
        : base(world)
    {
        Damage = damage;
        Net = net;
        PlayerMovement = movement;
        Hud = hud;
        Host = host;
    }

    /// <summary>snapping a disoriented player's eyes.</summary>
    private readonly IEnginePlayerMovement PlayerMovement;

    /// <summary>punching a disoriented player's view.</summary>
    private readonly IEngineHud Hud;

    /// <summary>the engine's random stream, for the disorienting delta.</summary>
    private readonly IEngineHost Host;

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IEngineNetworking Net;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="PhysExplosionVars"/>.</summary>
    [ScriptVars] public PhysExplosionVars Vars = new();

    /// <summary>
    /// env_physexplosion's own spawnflags, from physobj.cpp's SF_PHYSEXPLOSION_*.
    ///
    /// <para>
    /// Note that <see cref="NoDamage"/> does not mean "do nothing": the blast still
    /// pushes, it just stops hurting. Naming the whole set together is what keeps
    /// that readable at the call site, where the bit is handed to native as
    /// <c>physicsOnly</c>.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        NoDamage = 0b0000_0001,
        PushPlayer = 0b0000_0010,
        Radial = 0b0000_0100,
        TestLos = 0b0000_1000,
        DisorientPlayer = 0b0001_0000,
    }

    /// <summary>Falloff per unit of distance. Valve's constant, kept exactly.</summary>
    private const float Falloff = 1f / 2.5f;

    public readonly Output OnPushedPlayer = new();

    /// <summary>
    /// A radius of zero means "reach as far as the magnitude does" — 2.5 units
    /// per point, the same relationship combat damage uses.
    /// </summary>
    internal float EffectiveRadius => Vars.Radius > 0f ? Vars.Radius : Vars.Magnitude * 2.5f;

    /// <summary>
    /// Damage at a distance. Extracted because it is the part a mapper's
    /// numbers flow through and the part that is silently wrong if the falloff
    /// constant drifts: it falls off with DISTANCE, not with a fraction of the
    /// radius, and it never reaches zero.
    /// </summary>
    internal static float DamageAt(float magnitude, float distance)
        => Math.Max(1f, magnitude - distance * Falloff);

    [ScriptedEvent("Explode")]
    private void OnExplode() => Explode();

    /// <summary>Exposed for tests, which have no map to fire an input from.</summary>
    internal void Explode()
    {
        Vector origin = Origin;
        float radius = EffectiveRadius;

        Span<EntityHandle> found = stackalloc EntityHandle[MaxInRange];
        int count = Query(origin, radius, found);

        for (int i = 0; i < count; i++)
            Affect(found[i], origin);
    }

    private void Affect(EntityHandle target, Vector origin)
    {
        if (!Matches(target))
            return;

        var state = Net.EntityGetState(target, out MoveType moveType);

        // Must be able to take damage AND be something physics can move.
        // Both halves matter: a wall takes damage and cannot be pushed, a
        // decorative prop can be pushed and takes none.
        if ((state & EntityState.TakesDamage) == 0)
            return;

        if (moveType != MoveType.VPhysics && (state & EntityState.Physics) == 0)
            return;

        Vector spot = Entities.GetWorldSpaceCenter(target);
        Vector from = origin;

        // Squashed to a circle: the blast reaches as far sideways regardless of
        // height, which is what a mapper wants from an explosion in a corridor.
        if (Flags.HasFlag(SpawnFlag.Radial))
            from.Z = spot.Z;

        float distance = (from - spot).Length;

        // A radius of zero means unlimited here, NOT the derived radius above —
        // the query already limited who was found, so this second test only
        // applies when the mapper set an explicit one.
        if (Vars.Radius != 0f && distance > Vars.Radius)
            return;

        if (Flags.HasFlag(SpawnFlag.TestLos) && IsShielded(origin, target, Vars.InnerRadius))
            return;

        float damage = DamageAt(Vars.Magnitude, distance);

        if ((state & EntityState.Player) != 0 && Flags.HasFlag(SpawnFlag.PushPlayer))
        {
            PushPlayer(target, origin, spot);
            return;
        }

        Damage.TakeDamage(target, Handle, damage, DamageType.Blast,
                                direction: spot - from, forceOrigin: from,
                                physicsOnly: Flags.HasFlag(SpawnFlag.NoDamage));
    }

    /// <summary>
    /// A player is shoved rather than damaged, because damage force does not
    /// move one — the movement code recomputes velocity from their input every
    /// tick, so a blast has to go through base velocity to survive.
    /// </summary>
    private void PushPlayer(EntityHandle player, Vector origin, Vector spot)
    {
        Vector away = spot - origin;
        float distance = away.Length;
        Vector direction = away.Normalized;

        float falloff = PlayerFalloff(distance, Vars.Radius);

        if (Flags.HasFlag(SpawnFlag.DisorientPlayer))
            DisorientPlayer(player);

        Entities.AddBaseVelocity(player, direction * (Vars.Magnitude * falloff * 2f));
        OnPushedPlayer.Fire();
    }

    /// <summary>
    /// Stock's disorient (physobj.cpp:1013-1022): a random delta of up to 30
    /// degrees in pitch and yaw, the eyes snapped to THIS entity's angles plus
    /// it, and the view punched by it. It was an input nothing handles.
    /// </summary>
    private void DisorientPlayer(EntityHandle player)
    {
        var delta = new Vector(RandomInt(-30, 30), RandomInt(-30, 30), 0f);

        PlayerMovement.SnapEyeAngles(player, LocalAngles + delta);

        // CBasePlayer::ViewPunch on this one player: a punch centred on them
        // with no reach, in the air as well (TriggerImpact's form).
        Hud.ViewPunch(Entities.GetOrigin(player), delta, radius: 1f, inAir: true);
    }

    /// <summary>
    /// Stock's <c>random->RandomInt( lo, hi )</c>, inclusive, drawn from the
    /// engine's float stream as <see cref="PropData"/>'s is.
    /// </summary>
    private int RandomInt(int lo, int hi)
    {
        int i = (int)MathF.Floor(Host.RandomFloat(lo, hi + 1));
        return i < lo ? lo : (i > hi ? hi : i);
    }

    /// <summary>
    /// Full strength inside three quarters of the radius, tailing off to
    /// nothing at the edge.
    ///
    /// Unlike the damage falloff this one IS relative to the radius, which is
    /// Valve's inconsistency and is kept: a player and a crate side by side in
    /// the same blast are pushed on different curves.
    /// </summary>
    internal static float PlayerFalloff(float distance, float radius)
        => Remap(distance, radius, radius * 0.75f, 0f, 1f);

    /// <summary>
    /// The mapper's optional filter. A name first, then a classname — which is
    /// stock's order, and the reason EntityNames.PropPhysics works as a target even
    /// though nothing is called that.
    /// </summary>
    private bool Matches(EntityHandle target)
    {
        if (Vars.TargetName.Length == 0)
            return true;

        return string.Equals(Entities.GetName(target), Vars.TargetName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Entities.GetClassName(target), Vars.TargetName, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The map keys <see cref="PhysExplosion"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysExplosionVars
{
    public float Magnitude { get; set; }

    public float Radius { get; set; }

    /// <summary>Only affect entities with this name — or, failing that, this classname.</summary>
    [VarName("targetentityname")]
    public string TargetName { get; set; } = string.Empty;

    /// <summary>Blast starts this far out, so it is not shielded by its own housing.</summary>
    [VarName("inner_radius")]
    public float InnerRadius { get; set; }
}
