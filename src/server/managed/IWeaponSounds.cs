using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// A weapon's sound from the player holding it, under the prediction rules:
/// everyone in earshot hears it but the player whose client predicted the
/// command and played it already (<see cref="ServerSounds.EmitPredicted"/>).
/// </summary>
public interface IWeaponSounds
{
    void Emit(EntityHandle owner, string soundName);
}
