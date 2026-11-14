using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// TF2's weapon behaviours, one object per kind and side under the keys
/// <see cref="TFWeaponKinds.Key"/> names (the rows' <see cref="WeaponType.Behaviour"/>).
/// Both sides register the same set, so the client predicts with the
/// behaviour the server ran.
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFWeaponBehaviours(TFWeaponParts parts) : IWeaponBehaviourSource
{
    /// <summary>The behaviour that runs a kind.</summary>
    public IWeaponBehaviour For(TFWeaponKind kind) => kind switch
    {
        // The shotgun kind's own arms are the scattergun's (CTFScatterGun is
        // the family's behaviour; the plain shotguns answer the same class
        // with no knockback row to read).
        TFWeaponKind.Gun => new TFWeaponBehaviour(parts),
        TFWeaponKind.Shotgun => new TFScattergunBehaviour(parts),
        TFWeaponKind.Melee => new TFMeleeBehaviour(parts),
        TFWeaponKind.Minigun => new TFMinigunBehaviour(parts),
        TFWeaponKind.SniperRifle => new TFSniperRifleBehaviour(parts),
        TFWeaponKind.SniperRifleClassic => new TFSniperRifleClassicBehaviour(parts),
        TFWeaponKind.Revolver => new TFRevolverBehaviour(parts),
        TFWeaponKind.Projectile => new TFProjectileBehaviour(parts),
        TFWeaponKind.Bow => new TFBowBehaviour(parts),
        TFWeaponKind.Flamethrower => new TFFlamethrowerBehaviour(parts),
        TFWeaponKind.FlameBall => new TFFlameBallBehaviour(parts),
        TFWeaponKind.Medigun => new TFMedigunBehaviour(parts),
        TFWeaponKind.Lunchbox => new TFLunchboxBehaviour(parts),
        TFWeaponKind.Pda => new TFPDABehaviour(parts),
        TFWeaponKind.Builder => new TFBuilderBehaviour(parts),
        TFWeaponKind.BuffItem => new TFBuffItemBehaviour(parts),
        TFWeaponKind.RocketPack => new TFRocketPackBehaviour(parts),
        TFWeaponKind.Energy => new TFEnergyBehaviour(parts),
        TFWeaponKind.Inert => new TFInertBehaviour(parts),
        TFWeaponKind.LaserPointer => new TFWranglerBehaviour(parts),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The kinds this source provides.</summary>
    public static IEnumerable<TFWeaponKind> Kinds() => Enum.GetValues<TFWeaponKind>();

    public IEnumerable<KeyValuePair<string, IWeaponBehaviour>> Behaviours() =>
        Kinds().Select(kind => KeyValuePair.Create(TFWeaponKinds.Key(kind), For(kind)));
}
