namespace SourceSharp.Client;

/// <summary>
/// The local player's held-aside item meters, as a side published them — the
/// pair each carried weapon holstered with and the server second that was,
/// what the switch's restore advances into the deployed weapon's meter. The
/// client's half of the carried charge, as <see cref="ILocalWeaponClips"/>
/// is of the carried clip: without it the mirror knows only the hands' last
/// word, and the weapon that waited says nothing. A side with no publisher
/// answers false, and the meter the hands' last deploy left stands.
/// </summary>
public interface ILocalItemMeters
{
    /// <summary>The meter pair of weapon-catalog row <paramref name="typeIndex"/>; false when unpublished.</summary>
    bool MeterOf(int typeIndex, out float meter, out float stamp);
}
