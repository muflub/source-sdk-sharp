using SourceSharp.Abi;

using TF2.Server.Buildings;
using TF2.Server.Player;

using TF2.Shared;

namespace TF2.Tests.Player;

/// <summary>
/// The class-skill seams' no-op stands: a fact whose subject is one arm of
/// <see cref="TFClassSpecialSkill"/> hands the arms it never reaches the
/// inert half, as the composition hands the real ones.
/// </summary>
internal sealed class IdleSkillSpies : ITFSpyState
{
    public bool Swing(int ownerIndex, float range) => false;

    public bool Smack(int ownerIndex) => false;

    public bool ShouldDisguiseOnBackstab(int ownerIndex) => false;

    public bool DisguiseOnKill(int ownerIndex) => false;

    public void RemoveDisguise(int playerIndex) { }

    public void RemoveInvisibility(int playerIndex) { }

    public bool ActivateInvisibilityWatch(int playerIndex) => false;

    public bool FeignDeathHit(int victimIndex, EntityHandle attacker = default) => false;

    public bool KnifeReady(int ownerIndex, float now) => true;

    public void MeltKnife(int ownerIndex, float now, float seconds) { }

    public bool Disguise(int playerIndex, TFClass disguiseClass, TFTeam disguiseTeam) => false;

    public bool LastDisguise(int playerIndex, TFClass desired = TFClass.Undefined) => false;
}

/// <summary>The charge that never starts: the shield arm's stand.</summary>
internal sealed class IdleSkillShields : ITFShieldCharge
{
    public bool TryStart(int playerIndex) => false;

    public void EndSpecialAction(int playerIndex) { }
}

/// <summary>The lift that lifts nothing: the engineer arm's stand.</summary>
internal sealed class IdleSkillPickup : ITFBuildingPickup
{
    public bool CanPickup(EntityHandle player, TFBaseObject candidate) => false;

    public bool TryPickup(EntityHandle player) => false;

    public TFBaseObject? Carried(EntityHandle player) => null;
}
