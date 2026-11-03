namespace SourceSharp.Client;

/// <summary>
/// The local player's ammo pools, as a side published them — the client's half
/// of stock's per-player m_iAmmo (the DT_LocalPlayerExclusive array of
/// player.cpp:7929, received at c_baseplayer.cpp:228): the number any weapon
/// switch deploys with, the server's own, where without it the unheld weapon's
/// pool was remembered from its last hold or inferred from the spawn give. A
/// side with no publisher answers false and the mirror keeps its inference.
/// </summary>
public interface ILocalAmmoPools
{
    /// <summary>The pool of <paramref name="ammoName"/> for the local player; false when unpublished.</summary>
    bool PoolOf(string ammoName, out int count);
}
