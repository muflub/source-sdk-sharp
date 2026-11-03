using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The weapon selection from fakes (the ui-screen loop's sample step). The
// closed state — no selection, or the fastswitch modes the buckets arm
// never draws — shows nothing and so has no sample (the samples' runner
// refuses an empty frame); the adapter's and the core selection's facts
// hold those gates.

public static class TFWeaponSelectSamples
{
    public static UiScreen Build(UiSampleServices s, FakeWeaponSelect? select = null)
    {
        s.Add<ITFWeaponSelect>(select ?? new FakeWeaponSelect());
        // The name fit's measurer, when the run's services carry none of
        // their own.
        if (!s.Has<IUiFontMetrics>())
            s.Add<IUiFontMetrics>(new UiFontLibrary());
        return s.Create<TFWeaponSelect>();
    }

    // The soldier's three held slots, the shotgun selected: the large card
    // mid-column, the numbers and the labels on every card.
    [UiSample("the-soldiers-three-slots")]
    private static UiScreen Soldier(UiSampleServices s) => Build(s, new FakeWeaponSelect());

    // An empty magazine on the selection, on BLUE: the red note on the large
    // card and the team's border and art arm.
    [UiSample("empty-rocket-launcher-blue")]
    private static UiScreen EmptyRockets(UiSampleServices s)
    {
        var select = new FakeWeaponSelect { Team = 3, SelectedSlot = 0 };
        select.Slots[0] = select.Slots[0] with { CanSelect = false };
        return Build(s, select);
    }
}

/// <summary>The reads as the screen sees them: the soldier's three slots, the
/// rocket launcher selected, RED, every weapon selectable — the scripts'
/// own art names and printname tokens.</summary>
public sealed class FakeWeaponSelect : ITFWeaponSelect
{
    public bool Showing { get; set; } = true;

    public int SelectedSlot { get; set; } = 1;

    public int Team { get; set; } = 2;

    public readonly Dictionary<int, TFSelectionWeapon> Slots = new()
    {
        [0] = new("sprites/bucket_rocketlauncher", 0, 0, 200, 128, "#TF_Weapon_RocketLauncher", true),
        [1] = new("sprites/bucket_shotgun", 0, 0, 200, 128, "#TF_Weapon_Shotgun", true),
        [2] = new("sprites/bucket_fireaxe", 0, 0, 200, 128, "#TF_Weapon_FireAxe", true),
    };

    public bool SlotHasWeapon(int slot) => Slots.ContainsKey(slot);

    public TFSelectionWeapon WeaponInSlot(int slot) => Slots.TryGetValue(slot, out var weapon) ? weapon : default;
}
