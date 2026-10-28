using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// What a player's spray asks the engine to do once the managed PlayerSprays
/// has decided it (IShimTempEntBackendsClient::ClientPlayerLogoDecal,
/// shell_te_backends_client.cpp): find the logo material and the downloaded
/// texture by name, point the material at the texture, and shoot the decal on
/// the entity. Nothing is drawn when either name does not resolve.
/// </summary>
public interface IClientSprayEngine
{
    void PlayerLogoDecal(Vector position, int player, int entity, string material, string texture);
}
