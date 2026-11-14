using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Conditions;
using TF2.Shared.Econ;

namespace TF2.Shared;

/// <summary>
/// The decapitating melee's reach (CTFSword::GetSwingRange,
/// tf_weapon_sword.cpp:276-290, and the base class's own arm,
/// tf_weaponbase_melee.cpp:164-181): under the shield charge every melee
/// swing reaches 128; otherwise a sword reaches 72 — the base that the
/// melee-range multiplier then scales (tf_weaponbase_melee.cpp:420), so the
/// hook stands before the attribute hook in the chain.
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFSwordHook(AttributeHooks? attributes = null) : ITFWeaponHook
{
    /// <summary>The sword's row (<c>TF_WEAPON_SWORD</c>'s script).</summary>
    public const string SwordScript = "tf_weapon_sword";

    /// <summary>The sword's plain reach — "swords are typically 72" (tf_weaponbase_melee.cpp:177).</summary>
    public const float SwordRange = 72f;

    /// <summary>The charge's reach, the base class's own arm (tf_weaponbase_melee.cpp:167-170).</summary>
    public const float ChargedRange = 128f;

    /// <summary>
    /// The head cap (<c>MAX_DECAPITATIONS</c>, tf_shareddefs.h:1818): the
    /// health and speed bonuses stop growing here.
    /// </summary>
    public const int MaxHeads = 4;

    /// <summary>Each head's health, the TakeHealth grant and the
    /// GetSwordHealthMod factor (tf_weapon_sword.cpp:324, :358).</summary>
    public const int HealthPerHead = 15;

    /// <summary>Each head's speed, GetSwordSpeedMod's factor
    /// (tf_weapon_sword.cpp:302).</summary>
    public const float SpeedPerHead = 0.08f;

    /// <summary>The honourbound mark the katana family wears
    /// (<c>CTFKatana::IsHonorBound</c>'s attribute, tf_weapon_sword.cpp:548).</summary>
    public const string HonorBoundAttrib = "honorbound";

    /// <summary>The honourbound duel's floor: thrice the duelling victim's
    /// health (tf_weapon_sword.cpp:562).</summary>
    public const float DuelHealthScale = 3f;

    /// <summary>
    /// Before the attribute hook's +200 — the base the multiplier scales —
    /// and off the rules band's +190 (the round-timer warnings').
    /// </summary>
    public const int Order = 1_000_000 - 1_000 + 191;

    public int HookOrder => Order;

    private readonly AttributeHooks? _attributes = attributes;

    private bool IsSword(in TFWeaponHookContext weapon) =>
        string.Equals(weapon.Type.Name, SwordScript, StringComparison.Ordinal)
        || (_attributes?.Int(0, "is_a_sword", weapon.Item) ?? 0) != 0;

    public float ModifyMeleeRange(in TFWeaponHookContext weapon, float range)
    {
        if (!IsSword(in weapon))
            return range;

        return weapon.Conditions?.Has(TFCond.ShieldCharge) == true ? ChargedRange : SwordRange;
    }
}
