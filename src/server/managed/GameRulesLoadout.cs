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

/// <summary>
/// A mod's arm of a player's spawn (tf2 stage 2.3): what stock's
/// <c>CTFPlayer::Spawn</c> does around the kit — the class taken before it
/// (<c>ForceRespawn</c>'s <c>GetPlayerClass()->Init</c>), the health, the
/// model and the class's conditions after it (<c>InitClass</c>'s
/// <c>SetHealth( GetMaxHealth() )</c>, after <c>GiveDefaultItems</c>).
///
/// <para>
/// A <b>CHAIN</b> (<see cref="IHooks{T}"/>, asked in
/// <see cref="IOrderedHook.HookOrder"/>'s order) around the kit
/// <see cref="GameRules.OnPlayerSpawned"/> hands out:
/// <see cref="OnSpawning"/> before the <see cref="ILoadoutProvider"/>s are
/// asked — so a class chosen there is the class the kit is for — and
/// <see cref="OnSpawned"/> after the weapons and the ammo are in. A hook that
/// throws is named on the log and the spawn stands.
/// </para>
/// </summary>
public interface IPlayerSpawnHook
{
    /// <summary>The player has spawned; their kit is not yet handed out.</summary>
    void OnSpawning(EntityHandle player, float now);

    /// <summary>The player has spawned with their kit.</summary>
    void OnSpawned(EntityHandle player, float now);
}

/// <summary>
/// A veto on the spawn kit, as a hook (tf2 spawnfix):
/// <see cref="GameRules.OnPlayerSpawned"/> asks every gate after the
/// <see cref="IPlayerSpawnHook.OnSpawning"/> arms and before it hands out
/// anything, and any <c>false</c> skips the grant entire — no weapon, no
/// ammo, and no spawn-weapon choice either, since
/// <c>GiveDefaultItems</c> never ran — while the hooks keep running either
/// way. With no gates the game is unchanged. A gate that throws is logged
/// and does not veto, so a broken mod cannot leave a player weaponless (the
/// rule <see cref="IRespawnGate"/> applies to its throwers, for the same
/// reason).
///
/// <para>
/// The stock case is the unassigned arm of <c>CTFPlayer::Spawn</c>: the kit
/// arm — <c>InitClass()</c>, and its <c>GiveDefaultItems()</c>
/// (tf_player.cpp:4157) — runs only <c>if ( m_Shared.InState( TF_STATE_ACTIVE
/// ) )</c> (:3618), so a player spawning as fresh meat (<c>InitialSpawn</c>'s
/// <c>TF_STATE_WELCOME</c>, :3297) or moved to a watching team
/// (<c>ChangeTeam</c>'s unassigned arm, <c>TF_STATE_OBSERVER</c>, :6556)
/// never reaches it, and the kit the core hands unconditionally would hand
/// them the HL2 loadout. A class game must own that decision: it is who says
/// who plays and who watches.
/// </para>
/// </summary>
public interface IKitGate
{
    /// <summary>Whether <paramref name="player"/>'s spawn may have a kit. <c>false</c> gives them none.</summary>
    bool ShouldGiveKit(EntityHandle player);
}
