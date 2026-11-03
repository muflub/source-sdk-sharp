//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Anim;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Client.Player;

/// <summary>
/// The anim layer's weapon read on the client: the weapon in a player's hand,
/// its role <c>CTFWeaponBase::GetActivityWeaponRole</c>
/// (tf_weaponbase.cpp:4185-4205): the script's <c>WeaponType</c>, the base
/// item's <c>anim_slot</c> row over it. The local player's is the arsenal's
/// working copy; anyone else's is the carried weapon entity's decoded type,
/// which every client receives. A weapon the scripts do not name is stock's
/// no-weapon arm.
///
/// <para>
/// The disguise weapon answers for an enemy's eye as stock's
/// <c>CTFWeaponBase::ActivityList</c> does (tf_weaponbase.cpp:4212-4222): the
/// disguise class's own primary, the row
/// <c>TFDisguiseWeapons</c> dresses the carried weapon in.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFAnimWeapons)])]
public sealed class TFClientAnimWeapons(IEngineHud hud, ILocalArsenal arsenal, TFWeaponTypes types,
                                        TFWeaponScripts scripts, IClientStandalones standalones,
                                        WeaponCatalog weapons, ItemSchema schema, TFClassTable classes,
                                        TFClientCarriers carriers, TFClientPlayerResources resources) : ITFAnimWeapons
{
    public TFAnimWeapon? ActiveWeapon(int clientIndex)
    {
        if (clientIndex == hud.LocalPlayerIndex)
            return arsenal.TryGetActive(out string name, out _, out _) ? Row(name) : null;

        foreach (var entity in standalones.OfClass(EntityNames.SsWeapon))
        {
            if (entity is ClientSsWeapon weapon && weapon.State.OwnerIndex == clientIndex)
                return Row(weapons.ByIndex(weapon.State.WeaponTypeId)?.Name);
        }

        return null;
    }

    public TFAnimWeapon? DisguiseWeapon(int playerIndex)
    {
        if (playerIndex == hud.LocalPlayerIndex
            || carriers.Of(playerIndex) is not { } carrier
            || resources.Live is not { } resource
            || hud.LocalPlayerIndex >= TFPlayerResourceNetState.Slots
            || !ClientTFPlayerData.IsEnemyViewer(resource.State.Team[hud.LocalPlayerIndex],
                                                 resource.State.Team[playerIndex]))
            return null;

        var disguise = (TFClass)carrier.State.DisguiseClass;
        return schema.BaseItem(disguise, TFLoadoutSlot.Primary)?.ItemClass is { Length: > 0 } itemClass
            ? Row(TFItemClassTranslation.Translate(itemClass, classes[disguise]))
            : null;
    }

    private TFAnimWeapon? Row(string? name)
        => !string.IsNullOrEmpty(name) && scripts.Get(name) is { } script
            ? new TFAnimWeapon(types.ActivityWeaponRole(name, (TFWeaponRole)(int)script.Category), script.Id)
            : null;
}
