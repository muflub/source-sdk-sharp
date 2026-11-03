namespace SourceSharp.Client;

/// <summary>
/// The local player's carried clips, as a side published them — the client's
/// half of the carried weapon's stored clip (stock's <c>m_iClip1</c>, a field
/// of each weapon entity's send table, <c>basecombatweapon_shared.cpp</c>): the
/// number a weapon switch deploys with, the server's own, where without it the
/// unheld weapon's clip was remembered from its last hold or inferred from the
/// spawn give. A side with no publisher answers false and the mirror keeps its
/// own answer.
/// </summary>
public interface ILocalWeaponClips
{
    /// <summary>
    /// The clip of the carried weapon of catalog row <paramref name="typeIndex"/>;
    /// false when unpublished, and a published row past the carrier's width.
    /// </summary>
    bool ClipOf(int typeIndex, out int clip);
}
