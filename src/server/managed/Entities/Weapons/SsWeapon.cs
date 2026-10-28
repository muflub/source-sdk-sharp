using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The carried weapon, on the parallel hierarchy (Phase 10).
///
/// <para>
/// ONE PER PLAYER, holding whichever weapon is in their hands: the inventory
/// behind it is <see cref="Arsenal"/>'s data, and a switch changes this
/// entity's <see cref="WeaponNetState.WeaponTypeId"/> rather than swapping
/// entities. That is the collapse of stock's design — where every owned weapon
/// is a live entity so that the ACTIVE one can be predicted — into what was
/// actually load-bearing: the active one.
/// </para>
///
/// <para>
/// Effect kind: everyone must SEE it (the w_ model rides on the carrier), and
/// nothing may collide with it — the carrier's own hull is the thing bullets
/// hit. A weapon lying on the ground is a different thing entirely: a
/// <see cref="WeaponPickup"/>, which is an Item like any health kit.
/// </para>
///
/// <para>
/// Never placed by a map and never created by this class's own registration —
/// <see cref="Arsenal"/> creates one when a player first receives a weapon and
/// removes it when they leave.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.SsWeapon, ScriptedEntityKind.Effect)]
public sealed class SsWeapon : ScriptedEntity
{
    /// <summary>The weapon's replicated — and predicted — state.</summary>
    [NetState] public WeaponNetState State = new();

    public override void OnSpawn()
    {
        State.WeaponTypeId = -1;
        State.Clip = -1;

        // CBaseEntity::FollowEntity, which CBaseCombatWeapon::Equip attaches a carried weapon by.
        SetNotSolid(true);
    }

    // No dressing method: Arsenal points this entity at each weapon's w_ model
    // through IEngineEntityRender.SetModel, the same door Pickups uses, so the
    // dressing is observable where the switching is decided.
}

/// <summary>
/// The viewmodel — the arms and gun the carrying player sees at their own
/// eyes. Effect kind for the same reason as the weapon; created and owned by
/// <see cref="Arsenal"/> beside it.
///
/// <para>
/// Its model is the v_ model of whatever the weapon entity currently is, and
/// its <see cref="ViewModelNetState.Sequence"/> is written by the shared
/// weapon machine — on the server here, and on the client per replayed
/// command, which is what makes a fire animation start the frame the trigger
/// is pressed rather than a round trip later.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PredictedViewModel, ScriptedEntityKind.Effect)]
public sealed class SsViewModel : ScriptedEntity
{
    /// <summary>The viewmodel's replicated — and predicted — state.</summary>
    [NetState] public ViewModelNetState State = new();

    // CBaseEntity::FollowEntity, which CBasePlayer::CreateViewModel attaches it by.
    public override void OnSpawn() => SetNotSolid(true);

    // Dressed by Arsenal through IEngineEntityRender.SetModel on every switch;
    // see SsWeapon above for why the dressing lives on the switcher's side.
}
