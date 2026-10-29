using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Which side plays a weapon's sound, as stock's code places it:
/// DefaultReload's WeaponSound( RELOAD ) is under <c>#ifdef CLIENT_DLL</c> (the
/// crossbow's, the AR2's), the pistol's and the SMG1's own Reload
/// add one both sides play, and an animation event's sound is the server's
/// (Operator_HandleAnimEvent).
///
/// <para>
/// Found side by side with stock (tools/stock-ab --weapons, 2026-10-25): this
/// mod's server played the crossbow's reload, and its client predicted
/// BoltElectrify.
/// </para>
/// </summary>
public class WeaponSoundSideTests
{
    private const int ReloadKey = (int)InputButtons.Reload;

    private sealed class Arms : IWeaponArms
    {
        public readonly List<string> Shared = [];
        public readonly List<string> ClientOnly = [];

        public void Sound(string soundScript) { if (soundScript.Length > 0) Shared.Add(soundScript); }
        public void ClientSound(string soundScript) => ClientOnly.Add(soundScript);
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) { }
        public void MeleeSwing(in WeaponType type) { }
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) { }
        public int LookupSequence(WeaponAnim anim) => (int)anim + 1;
        public float AnimDuration(WeaponAnim anim) => 1f;
    }

    private static Arms Reloaded(string weapon, int clip, int clipSize)
    {
        var arms = new Arms();
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();
        w.WeaponTypeId = WeaponCatalog.Stock().IndexOf(weapon);
        w.Clip = clip;
        w.Reserve = clipSize;
        new WeaponLogic(arms).RunCommand(w, vm, ReloadKey, 10f, effectsLive: true);
        return arms;
    }

    [Fact]
    public void AnAr2sReloadSoundIsTheClientsAlone()
    {
        var arms = Reloaded("weapon_ar2", clip: 2, clipSize: 30);

        Assert.Equal(new[] { "Weapon_AR2.Reload" }, arms.ClientOnly);
        Assert.Empty(arms.Shared);
    }

    [Fact]
    public void ACrossbowsReloadSoundIsTheClientsAlone()
    {
        var arms = Reloaded("weapon_crossbow", clip: 0, clipSize: 1);

        Assert.Equal(new[] { "Weapon_Crossbow.Reload" }, arms.ClientOnly);
        Assert.Empty(arms.Shared);
    }

    [Fact]
    public void APistolsReloadAlsoPlaysOnBothSides()
    {
        var arms = Reloaded("weapon_pistol", clip: 2, clipSize: 18);

        Assert.Equal(new[] { "Weapon_Pistol.Reload" }, arms.ClientOnly);
        Assert.Equal(new[] { "Weapon_Pistol.Reload" }, arms.Shared);
    }
}
