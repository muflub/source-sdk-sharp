//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.WeaponEffects;

using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Client.Weapons;

/// <summary>
/// Every TF gun's world-model muzzle flash: <c>CTFWeaponBase::CreateMuzzleFlashEffects</c>
/// (<c>shared/tf/tf_weaponbase.cpp:3113-3193</c>) as the flash parity rides it —
/// <c>CTFWeaponBaseGun::DoFireEffects</c> bumps <c>m_nMuzzleFlashParity</c> once per
/// shot (<c>tf_weaponbase_gun.cpp:1008-1018</c>), the client's
/// <c>ProcessMuzzleFlashEvent</c> compares it against the last seen
/// (<c>c_baseanimating.h:620-622</c>, <c>tf_weaponbase.cpp:3401</c>) and fires the
/// script's <c>MuzzleFlashParticleEffect</c> on the bearer's <c>muzzle</c>
/// attachment, <c>PATTACH_POINT_FOLLOW</c> (<c>DispatchMuzzleFlash</c>, :3195).
/// The base's 1st/3rd-person effect names are virtual and answered by none
/// (<c>tf_weaponbase.h:464-469</c>): one script name, both views — the bearer is
/// <c>GetAppropriateWorldOrViewModel</c>'s, the viewmodel in the first person and
/// the world model ever else.
///
/// <para>
/// STOCK'S PARITY IS NOT THIS PORT'S <see cref="WeaponNetState"/>, so the shot is
/// the same state edge every draw effect reads — the frame the next shot is pushed
/// later AND a round is gone. The first-person viewer's flash is the predicted
/// replay's, <c>IWeaponArms.MuzzleFlash</c> on the viewmodel attachment, so the
/// frame the camera is at the eye hangs nothing here: the same shot never wears
/// two. The remote guns read the networked state, whose edge is the parity's
/// arrival.
/// </para>
///
/// <para>
/// Stock's two <c>ShouldDoMuzzleFlash</c>-false kinds (<c>tf_weapon_minigun.h:78</c>,
/// <c>tf_weapon_compound_bow.h:73</c>) never reach the parity: the minigun's firing
/// flash is <see cref="TFMinigunLoop"/>'s constant system and the bow's launch asks
/// neither, so both are named vetoes here.
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class TFWeaponMuzzleFlash(TFWeaponScripts scripts, WeaponCatalog weapons,
                                        IEngineEffects effects, IEngineEntityRender render)
    : IWeaponDrawEffect
{
    /// <summary>The attachment the flash hangs off, on the world model (:3127, :3197).</summary>
    public const string Attachment = "muzzle";

    /// <summary>One gun's last seen kind and shot clocks, the edge's before.</summary>
    private sealed class Gun
    {
        public int Type = -1;
        public bool Seen;
        public float NextAttack;
        public int Clip;
    }

    private readonly Dictionary<EntityHandle, Gun> _guns = [];

    /// <summary>The empty name is every kind's: the frame runs this beside the named effect, as the parity path is the base's, not one class's.</summary>
    public string WeaponName => "";

    public bool TwoPass => false;

    /// <summary>The flash is a particle; nothing draws in the viewmodel pass.</summary>
    public void ViewModelDrawn(in WeaponDrawContext context) { }

    /// <summary>The flash is a particle; the model draws.</summary>
    public int DrawModel(in WeaponDrawContext context, int flags) => -1;

    public void Think(in WeaponDrawContext context)
    {
        EntityHandle weapon = context.Weapon.Handle;
        if (!_guns.TryGetValue(weapon, out Gun? gun))
            _guns[weapon] = gun = new Gun();

        int type = context.State.WeaponTypeId;
        float next = context.State.NextPrimaryAttack;
        int clip = context.State.Clip;
        bool shot = gun.Seen && gun.Type == type && next != gun.NextAttack && clip < gun.Clip;
        gun.Type = type;
        gun.Seen = true;
        gun.NextAttack = next;
        gun.Clip = clip;

        // The first-person viewer's flash is the predicted replay's; the world
        // model is the bearer ever else, the local third person included.
        if (!shot || context.FirstPerson)
            return;

        string? effect = weapons.ByIndex(type) is { } weaponType
            && !Vetoes(weaponType.Name)
            ? scripts.Get(weaponType.Name)?.MuzzleFlashParticleEffect
            : null;

        if (string.IsNullOrEmpty(effect))
            return;

        int muzzle = render.LookupAttachment(weapon, Attachment);
        if (muzzle <= 0 || !effects.ParticleSystemStart(weapon, effect))
            return;

        effects.ParticleSystemFollowEntity(weapon, 0, weapon, muzzle);
    }

    /// <summary>Stock's two false overrides, whose flashes their own arms carry.</summary>
    private static bool Vetoes(string name) => name is "tf_weapon_minigun" or "tf_weapon_compound_bow";

    /// <summary>The gun is gone: the next sight of it re-arms from nothing.</summary>
    public void Forget(EntityHandle weapon) => _guns.Remove(weapon);
}
