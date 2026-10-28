using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// The local player as <see cref="WeaponSelectionHud"/> reads and moves them:
/// the networked selection bits on their state, the weapon in hand, a choice
/// sent to the server's Arsenal (<c>ss_use</c>, <c>lastinv</c>) and the
/// selection's sounds from the player.
/// </summary>
public sealed class LocalWeaponSelection(IEngineHud screen, IClientStandalones standalones, ILocalArsenal arsenal,
                                         WeaponCatalog weapons, IWeaponSelectionCommand send,
                                         Sounds.IClientEntitySounds sounds)
    : IWeaponSelectionPlayer, IWeaponSelectionActions
{
    private ClientSsPlayer? Local => standalones.GetByIndex(screen.LocalPlayerIndex) as ClientSsPlayer;

    public bool Exists => Local is not null;

    public bool HudAllowed => Local is { State.Alive: true };

    public bool FrozenOrDead => Local is not { } p || !p.State.Alive || (p.State.Flags & GameMovement.FlFrozen) != 0;

    public uint Carried => Local?.State.CarriedWeapons ?? 0u;

    public uint Empty => Local?.State.EmptyWeapons ?? 0u;

    public int Active => arsenal.TryGetActive(out string name, out _, out _) ? weapons.IndexOf(name) : -1;

    public void Select(int weaponType)
    {
        if (weapons.ByIndex(weaponType) is { } weapon)
            send.SendSelection("ss_use " + weapon.Name);
    }

    public void LastWeapon() => send.SendSelection("lastinv");

    public void Sound(string soundName)
    {
        if (Local is { } player)
            sounds.EmitFrom(player.Handle, soundName);
    }
}

/// <summary>Each weapon type's bucket and bucket_position, from its script, read once per type.</summary>
public sealed class ScriptWeaponSlots(WeaponCatalog weapons, WeaponScriptDatabase scripts) : IWeaponSlots
{
    private int[] _slot = [];
    private int[] _position = [];

    public int Count => weapons.Count;

    public int Slot(int weaponType) => Row(weaponType) ? _slot[weaponType] : -1;

    public int Position(int weaponType) => Row(weaponType) ? _position[weaponType] : -1;

    private bool Row(int weaponType)
    {
        if (weaponType < 0 || weaponType >= weapons.Count)
            return false;

        if (_slot.Length != weapons.Count)
        {
            _slot = new int[weapons.Count];
            _position = new int[weapons.Count];
            for (int t = 0; t < _slot.Length; t++)
            {
                WeaponScript? script = weapons.ByIndex(t) is { } type ? scripts.ReadScript(type.Name) : null;
                _slot[t] = script?.Slot ?? -1;
                _position[t] = script?.Position ?? -1;
            }
        }

        return true;
    }
}
