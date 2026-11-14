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

    /// <summary>
    /// The same emit with the script's pitch scaled by the multiplier on top:
    /// the bow's pull sound carries its refire rate there, stock's
    /// <c>CTFCompoundBow</c> pull (<c>tf_weapon_compound_bow.cpp:243-248</c>).
    /// The default keeps the plain sound, so an ear without pitch behaves as
    /// it did. The pitch lands on the predicted emit in ServerSounds.
    /// </summary>
    void Emit(EntityHandle owner, string soundName, float pitchMultiplier) => Emit(owner, soundName);
}
