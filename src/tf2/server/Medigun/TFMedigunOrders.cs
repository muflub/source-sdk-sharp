namespace TF2.Server.Medigun;

/// <summary>
/// The medigun and healing stages (Stage 2.9's band, +900..+929 of TF2's
/// thousand, below the mods' <c>ModStages.First</c>): every ordered hook and
/// lifecycle participant of this lane takes one of these, so none ties another
/// (TF2LifecycleOrderTests).
/// </summary>
public static class TFMedigunOrders
{
    private const int Band = 1_000_000 - 1_000 + 900;

    /// <summary>The players' heal components: the accumulation, the overheal decay, the charge expiry and the beam watchdog (<c>TFMedigunComponents</c>).</summary>
    public const int Components = Band + 0;

    /// <summary>The healing state's level reset and departures (<c>TFPlayerHealing</c>).</summary>
    public const int Healing = Band + 10;

    /// <summary>The medigun states' level reset and departures (<c>TFMedigunWeapon</c>).</summary>
    public const int Weapon = Band + 20;

    /// <summary>The dev path that heals a target (<c>TFMedigunDevCommands</c>, deferred in docs/plans/tf2/deferred.tsv).</summary>
    public const int DevCommands = Band + 25;
}
