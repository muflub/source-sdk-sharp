using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// Who hands the player their weapons and ammo (tf2 stage 0.7 part C).
///
/// <para>
/// A <b>CHAIN</b> (<see cref="IHooks{T}"/>, asked in
/// <see cref="IOrderedHook.HookOrder"/>'s order): a class game, a map-mode
/// that arms everyone with one weapon, and a fun-mod's override all want to
/// say something about the spawn kit, and none of them replaces the others
/// at the source.
/// </para>
///
/// <para>
/// <b>WHAT REPLACES WHAT.</b> Each column — weapons, ammo — is answered whole
/// or not at all: a provider that names any weapon owns the weapons column
/// and the core's HL2 three are gone; a provider that names no weapon at all
/// leaves the core's answer standing. Anything less strict reads as intended
/// on purpose — "everyone gets a shotgun" that quietly keeps the crowbar is
/// the kind of plausible wrong kit a per-entry union invites.
/// </para>
///
/// <para>
/// <b>LAST ANSWER WINS PER SLOT</b>, as the plan has it: a "slot" is a named
/// thing — one weapon class name, one ammo type — and within one provider's
/// own answer, a later entry naming the same thing wins over an earlier one,
/// so a mod that adjusts one count does not have to restate the rest. Two
/// providers that both name <c>weapon_pistol</c> hand it out ONCE (the grants
/// are merged by name before they reach the engine); two that set a count on
/// the same ammo type leave the later provider's count with the player. The
/// key is the NAME — the only key this build has for a weapon before the
/// entity classes exist; when Arsenal's per-slot weapon table grows a
/// spawn-time reader, the slot becomes that row and this paragraph changes
/// with it.
/// </para>
///
/// <para>
/// A provider that throws is named on the log and treated as having answered
/// nothing — the kit the player would have had without the broken mod is
/// what a broken mod must leave behind (the rule the respawn gates and the
/// damage chain apply to their throwers, for the same reason).
/// </para>
/// </summary>
public interface ILoadoutProvider
{
    /// <summary>
    /// The weapon class names to hand <paramref name="player"/>. An empty
    /// answer is no opinion on the column — the core's HL2 three survive.
    /// </summary>
    IEnumerable<string> Weapons(EntityHandle player);

    /// <summary>
    /// The ammo to hand out alongside them. An empty answer is no opinion on
    /// the column — the core's starting ammo survives.
    /// </summary>
    IEnumerable<(string Ammo, int Count)> Ammo(EntityHandle player);
}
