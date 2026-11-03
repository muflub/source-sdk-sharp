//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Weapons;

namespace TF2.Server.Entities;

/// <summary>
/// env_sniperdot: the dot at the end of a zoomed sniper's aim (stock's
/// <c>CSniperDot</c>, <c>server/tf/tf_weapon_sniperrifle.cpp:1381-1484</c>).
/// One per zoomed player, created by
/// <see cref="TF2.Server.Weapons.TFZoomEffects"/> on the zoom-in and
/// destroyed on the zoom-out. Each think traces from the owner's
/// eye along their aim to <c>MAX_TRACE_LENGTH</c> and sits where the trace
/// stops (<c>UpdateSniperDot</c> :964-999), naming what it stopped on —
/// stock's <c>m_hTargetEnt</c>, the world and nothing damageable excluded —
/// and the surface's normal. An owner who is gone or dead takes the dot with
/// them (<c>CSniperDot::Update</c>'s guard, :1471-1476).
///
/// <para>
/// The client's draw reads the wire state <see cref="State"/> carries
/// (TF2.Shared.Weapons.SniperDotNetState): the owner and its team, and
/// <c>m_flChargeStartTime</c> — stock's DT_SniperDot, set once at the creation
/// the zoom is (<c>ResetChargeTime</c>, tf_weapon_sniperrifle.h:48).
/// <c>m_vecSurfaceNormal</c> and <c>m_hTargetEnt</c> network nowhere in stock
/// and stay here.
/// </para>
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Effect)]
[Precache(PrecacheKind.Material, "effects/sniperdot_red.vmt")]
[Precache(PrecacheKind.Material, "effects/sniperdot_blue.vmt")]
public sealed class SniperDot(IEnginePlayers players, IEngineWorldQuery world) : ScriptedEntity
{
    /// <summary>Stock's <c>env_sniperdot</c> class name (tf_weapon_sniperrifle.cpp:1383).</summary>
    public const string MapClass = "env_sniperdot";

    /// <summary>Source's MAX_TRACE_LENGTH: the diagonal of the world cube.</summary>
    private const float MaxTraceLength = 56756f;

    /// <summary>The zoomed player this dot marks, named by <see cref="Begin"/>.</summary>
    public EntityHandle Owner { get; set; } = EntityHandle.None;

    /// <summary>What the last aim trace stopped on that is not the world and can take damage (stock's <c>m_hTargetEnt</c>).</summary>
    public EntityHandle Target { get; private set; } = EntityHandle.None;

    /// <summary>The normal of the surface the last aim trace stopped on (stock's <c>m_vecSurfaceNormal</c>).</summary>
    public Vector Normal { get; private set; }

    /// <summary>
    /// Stock's <c>m_flChargeStartTime</c>, before it is on the wire: the
    /// creation moment, written by <see cref="TF2.Server.Weapons.TFZoomEffects"/>
    /// the way <c>ResetChargeTime</c> reads the clock in stock's constructor.
    /// </summary>
    public float ChargeStartTime { get; set; }

    /// <summary>The wire state: the draw's owner, team and charge clock.</summary>
    [NetState] public SniperDotNetState State = new();

    /// <summary>
    /// The create-time dress, the spawner's call once the spawn has made the
    /// instance: the directory learns the entity from the spawn's callback
    /// (<see cref="ScriptedEntity.Spawn"/>), so no property can be set before
    /// it — the dropped weapon's <c>Init</c>, not the
    /// keyvalues-before-spawn order.
    /// </summary>
    public void Begin(EntityHandle owner, float chargeStart)
    {
        Owner = owner;
        ChargeStartTime = chargeStart;

        State.OwnerIndex = Owner == EntityHandle.None ? 0 : Entities.GetIndex(Owner);
        State.Team = (byte)players.GetTeam(Owner);
        State.ChargeStartTime = ChargeStartTime;

        // CSniperDot::Create's SetOwnerEntity (tf_weapon_sniperrifle.cpp:1457).
        Entities.SetOwner(Handle, Owner);

        SetNextThink(0f);
    }

    public override void OnThink()
    {
        // No owner, or a dead one: the dot has no reason to exist
        // (UpdateSniperDot's pPlayer guard, tf_weapon_sniperrifle.cpp:964-999).
        if (!Entities.IsValid(Owner) || !players.IsAlive(Owner))
        {
            Remove();
            return;
        }

        Vector from = Entities.GetEyePosition(Owner);
        Vector to = from + Angles.Forward(Entities.GetEyeAngles(Owner)) * MaxTraceLength;
        var hit = world.TraceLine(from, to, TraceMask.ShotNoWindows, Owner);

        Entities.SetOrigin(Handle, hit.End);

        // The world is entry 0: named on purpose (EntityHandle's rule).
        Target = hit.Hit && hit.Entity != EntityHandle.None && hit.Entity.EntryIndex != 0
            ? hit.Entity
            : EntityHandle.None;
        Normal = hit.Normal;

        SetNextThink(0f);
    }
}
