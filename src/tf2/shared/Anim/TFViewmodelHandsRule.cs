//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Shared.Anim;

/// <summary>
/// The hands arm of stock's <c>CTFWeaponBase::GetViewModel</c>
/// (tf_weaponbase.cpp:651-676) as an <see cref="IViewmodelModelRule"/>: for
/// an item that attaches to hands, the viewmodel wears the PLAYER CLASS's
/// hand model (<c>GetPlayerClass()->GetHandModelName</c>), not the weapon's
/// own display model.
///
/// <para>
/// What "attaches to hands" means is the item schema's <c>attach_to_hands</c>
/// key (econ_item_schema.cpp:3166, parsed into
/// <see cref="TF2.Shared.Econ.ItemDefinition.AttachToHands"/>): "If set, we
/// use the base hands model for a viewmodel, and bonemerge the above player
/// model" (econ_item_schema.h:1546). The class's model is the class script's
/// <c>model_hands</c> (<see cref="TFClassData.ModelHands"/>). A weapon whose
/// item does not attach, a classless player, or a class without hands
/// answers no opinion — stock's other arm, <c>GetTFWpnData().szViewModel</c>,
/// which is the core row's own model. The weapon's own c_model rides the
/// hands as the viewmodel attachment (stock's
/// <c>C_ViewmodelAttachmentModel</c>).
/// </para>
///
/// <para>
/// Registered on the SERVER: the model is the entity's networked state, not
/// predicted input.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFViewmodelHandsRule(TFWeaponTypes types, TFClassTable classes, ITFWeaponOwners? owners = null) : IViewmodelModelRule, IOrderedHook
{
    /// <summary>The rule's order: the weapons band, after the act table (+220).</summary>
    public const int Order = 1_000_000 - 1_000 + 221;

    private readonly ITFWeaponOwners _owners = owners ?? new NoTFWeaponOwners();

    /// <inheritdoc />
    public int HookOrder => Order;

    /// <inheritdoc />
    public string? Model(int playerIndex, WeaponType type, string? chosen)
    {
        if (types.BaseItemFor(type.Name) is not { AttachToHands: true })
            return chosen;

        TFClass cls = _owners.ClassOf(playerIndex);
        if (cls <= TFClass.Undefined || cls >= TFClass.CountAll)
            return chosen;

        string hands = classes[cls].ModelHands;
        return hands.Length > 0 ? hands : chosen;
    }
}
