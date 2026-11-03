using System.Reflection;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Boundary;

/// <summary>
/// What the HL2MP binding may call in the core (plan_hl2mp_ai §3.3): an IL
/// census over SourceSharp.Ai.Hl2mp, every call into a SourceSharp assembly
/// that is not the AI's own, held to an exact list. A bot acts only by the
/// usercmd it sends through the human path; a call that places, turns or
/// teleports a player (or any callee not listed) is red, and so is a listed
/// one that went, so the list stays the truth.
/// </summary>
public class ApplierAllowListTests
{
    /// <summary>
    /// The calls the binding makes into the core, by caller. The applier
    /// itself only writes a <c>UserCmd</c> and hands it to the runner.
    /// </summary>
    private static readonly string[] Allowed =
    [
        // AiAdminMenu: the bots' place in the admin menu, data whose actions are the roster's own.
        "AdminNode::.ctor", "AdminNode::Folder",
        // Hl2mpBots and the commands: the core's bots, and the log.
        "BotPlayer::get_EntIndex", "BotPlayer::get_Name", "BotPlayer::get_Player",
        "EntityHandle::get_None", "EntityHandle::op_Equality", "EntityHandle::op_Inequality",
        "IBotPlayers::Add", "IBotPlayers::Kick", "IBotPlayers::get_Bots",
        // EngineBotSelf: the bot's own origin, eye, view and life (§2.4); ArsenalBotInventory: its own holdings, as its HUD lists them.
        "IEngineEntities::GetClassName", "IEngineEntities::GetEyeAngles", "IEngineEntities::GetEyePosition",
        "IEngineEntities::GetOrigin", "IEnginePlayers::GetObserverMode", "Arsenal::Holdings",
        "IEnginePlayers::IsAlive", "WeaponCatalog::IndexOf",
        // EngineBotSelf: whether the bot is on a ladder, its own move type as its client predicts it (Phase 5's ladders).
        "IEngineNetworking::EntityGetState",
        // The tick a bot's command runs on.
        "IEngineFrame::TickCount",
        "ILog::Msg", "ILog::Warning",
        // The applier: the human path (§3.2) and the command's angles.
        "IPlayerCommandRunner::Queue", "IPlayerCommandRunner::RunQueued",
        "UserCmd::set_Pitch", "UserCmd::set_Roll", "UserCmd::set_Yaw",
        // CommandTap: every player's command's view angles, read into the recording (§3.4.4).
        "UserCmd::get_Pitch", "UserCmd::get_Roll", "UserCmd::get_Yaw",
        // Hl2mpNavNumbers: the movement code's step, jump and fall numbers (Phase 2).
        "Movement::get_Current",
        // Hl2mpNav: the level's files through the file seam, the cache under the game dir (Phase 2 step 3).
        "IEngineFiles::ReadGameFileBytes", "IModInfo::get_GameDir",
        // GameFileBotNames: the name pools' data file; Hl2mpBots: a bot's skill label beside its name (user rulings 2026-11-01).
        "IEngineFiles::ReadGameFile", "IScoreboardTags::Set",
        // EngineObstacleEyes: an obstacle's entity, found by name or class and place, alive, where and how turned.
        "IEngineEntities::FindAllByClassname", "IEngineEntities::FindByName", "IEngineEntities::GetAngles",
        "IEngineEntities::GetWorldSpaceCenter", "IEngineEntities::IsValid",
        // EngineWorldProbe: the validator's hull traces (§6.3), evidence only.
        "ITraceQueries::Hull", "TraceAnswer::get_Fraction", "TraceAnswer::get_StartSolid", "TraceAnswer::get_Entity",
        // ai_nav_probe: the ground under a point, by hand.
        "ITraceQueries::Line", "TraceAnswer::get_End", "TraceAnswer::get_Hit",
        "IEngineEntities::GetIndex", "Vector::.ctor",
        // ActorDirectory: the living players by slot, a human's actor from its handle (Phase 4 step 1).
        "IEngineEnvironment::get_Globals", "Globals::get_IsAvailable", "Globals::get_MaxClients", "IEnginePlayers::GetHandle",
        "EntityHandle::get_Value",
        // VisionPass (§4.1): other players' origin, eye, velocity and the bot's view; the engine's PVS; the sight lines.
        "IEngineEntities::GetVelocity", "IEnginePlayers::BoxInPvs",
        // SnapshotBuilder: the weapon model a client sees in another's hands (§4.3); EngineBotHud: the bot's own HUD.
        "IEnginePlayers::GetActiveWeapon", "IEngineEntities::GetHealth", "IEngineEntities::GetArmor",
        "Arsenal::PoolOf", "WeaponCatalog::For", "WeaponType::get_AmmoName",
        // SoundTap (§4.2): the emitted sound as its recipients are sent it; SoundClassifier: the weapon table's sound names.
        "EmittedSound::get_Origin", "EmittedSound::get_Owner", "EmittedSound::get_Recipients", "EmittedSound::get_SoundLevel",
        "EmittedSound::get_SoundName", "SoundRecipients::SoundLevelToAttenuation",
        "WeaponCatalog::ByIndex", "WeaponCatalog::get_Count", "WeaponType::get_EmptySound", "WeaponType::get_FireSound",
        "WeaponType::get_ReloadSound", "WeaponType::get_Secondary", "SecondaryFire::get_Sound",
        // CombatTap (Phase 4 step 4): a hit and a death as the hooks tell them; the scoreboard's kills and deaths (the player resource's).
        "PlayerHurt::get_Attacker", "PlayerHurt::get_HealthLeft", "PlayerHurt::get_Player", "PlayerHurt::get_Taken",
        "PlayerDeath::get_Attacker", "PlayerDeath::get_Player", "IPlayerDirectory::get_All", "PlayerRecord::get_Deaths", "PlayerRecord::get_Kills",
        // Hl2mpAiSession (§5.0): the respawn convars, the reload animation on the view model, the weapon table's projectile and behaviour; an opponent's user id.
        "ConVarRegistry::Get", "ConVar`1::get_Value", "ModelDescriptors::ForModel", "ModelDescriptor::LookupSequenceByActivity",
        "ModelDescriptor::SequenceDuration", "WeaponType::get_ViewModel", "WeaponType::get_Behaviour", "WeaponType::get_Projectile",
        "WeaponType::get_LaunchSpeed", "WeaponType::get_ClipSize", "WeaponType::get_Interval", "WeaponType::get_Name",
        "IEnginePlayers::GetUserId",
        // The aim range's cheats, measuring only (§12.3): ai_bot_kit's one weapon and reserve, ai_aim_tough's health.
        "Arsenal::GiveAmmo", "Arsenal::GiveWeapon", "Arsenal::Strip", "Arsenal::Switch", "IEngineEntities::SetHealth",
        // Hl2mpBotChat (§3.6): the kill feed's facts, a line said, who would hear it and its say path; a player's name as every client shows it.
        "DeathNoticeFacts::get_Scorer", "DeathNoticeFacts::get_Victim", "DeathNoticeFacts::get_Weapon",
        "PlayerChat::get_Speaker", "PlayerChat::get_TeamOnly", "PlayerChat::get_Text",
        "HostSay::CanHear", "HostSay::Say", "IClientInfo::ClientConVar",
        // The binding's own convars (ai_threads, ai_sync, §2.7): their base constructors.
        "IntConVar::.ctor", "BoolConVar::.ctor",
        // EngineServerSlots: the people on the server, for the roster's free slots (§3.5).
        "IClientInfo::Address", "IEngineHost::get_IsDedicatedServer",
        // AiBotRosterService (Phase 5c): the roster convar, declared, read and cleared at the level's start.
        "StringConVar::.ctor", "ConVar`1::SetValue",
        // AiWorldRoutes (§7.4): a human's name, for the debug view's label.
        "PlayerNames::NameOf",
    ];

    /// <summary>Calls that move or turn a player outside its usercmd (§3.3): never, from any caller.</summary>
    private static readonly string[] Forbidden =
    [
        "IEnginePlayers::PlaceAtSpawn", "IEnginePlayers::SnapEyeAngles", "IEnginePlayers::Respawn",
        "IEnginePlayers::SetMoveType", "IEnginePlayers::ForceButtons", "IEnginePlayers::SetFrozen",
        "IEngineEntities::SetOrigin", "IEngineEntities::SetAngles", "IEngineEntities::SetVelocity", "IEngineEntities::Teleport",
    ];

    private static readonly Assembly Binding = typeof(BotCommandApplier).Assembly;

    private static bool IsCore(MethodBase m)
    {
        string name = m.DeclaringType?.Assembly.GetName().Name ?? "";
        // The core is this repo's: map-tools' reader (SourceSharp.MapFormats) is a package the binding reads files with.
        return name.StartsWith("SourceSharp.", StringComparison.Ordinal) && !name.StartsWith("SourceSharp.Ai", StringComparison.Ordinal)
               && name != "SourceSharp.MapFormats";
    }

    private static string[] CoreCalls()
    {
        var calls = new SortedSet<string>(StringComparer.Ordinal);
        IlCallCensus.Calls(m =>
        {
            if (IsCore(m))
                calls.Add($"{m.DeclaringType!.Name}::{m.Name}");
            return IsCore(m);
        }, Binding);
        return calls.ToArray();
    }

    private static string[] CoreCallsBy(string caller)
        => IlCallCensus.Calls(IsCore, Binding).Where(c => c.Caller == caller).Select(c => c.Callee).Distinct().Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void TheBindingCallsExactlyTheListedCoreMembers()
    {
        string[] calls = CoreCalls();
        Assert.True(Allowed.Order(StringComparer.Ordinal).SequenceEqual(calls), "the binding's core calls: " + string.Join(", ", calls.Select(c => $"\"{c}\"")));
    }

    [Fact]
    public void TheApplierCallsOnlyTheRunnerAndTheCommandsSetters()
    {
        string[] calls = CoreCallsBy(typeof(BotCommandApplier).FullName!);
        Assert.True(ApplierAllowed.SequenceEqual(calls), "the applier's core calls: " + string.Join(", ", calls.Select(c => $"\"{c}\"")));
    }

    /// <summary>The applier's own: the runner, the command's angle setters, and handle comparisons.</summary>
    private static readonly string[] ApplierAllowed =
    [
        "Queue", "RunQueued", "get_None", "op_Inequality", "set_Pitch", "set_Roll", "set_Yaw",
    ];

    [Fact]
    public void NoCallerMovesOrTurnsAPlayerOutsideItsCommand()
        => Assert.Empty(CoreCalls().Intersect(Forbidden));

    [Fact]
    public void TheCensusSeesTheRunner()
        // The positive arm: the read finds the applier's own core calls.
        => Assert.Contains("IPlayerCommandRunner::RunQueued", CoreCalls());
}
