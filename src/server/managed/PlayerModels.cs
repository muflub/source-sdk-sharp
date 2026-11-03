//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A mod whose players wear its own models (TF2's classes): the mere presence of
/// one turns off <see cref="PlayerModels"/>, so cl_playermodel means nothing in it
/// and HL2MP's models are not precached for it.
/// </summary>
public interface IPlayerModelOwner
{
}

/// <summary>
/// What a player wears: <c>CHL2MP_Player::SetPlayerModel</c>,
/// <c>SetPlayerTeamModel</c> and <c>ValidatePlayerModel</c>, and the model half of
/// <c>CHL2MPRules::ClientSettingsChanged</c> that <see cref="PlayerNames"/> leaves out.
///
/// <para>
/// A model is chosen once, at the first spawn (stock's PickDefaultSpawnTeam, which
/// asks only while the player has none), and kept through respawns; changing it after
/// that goes through <see cref="SettingsChanged"/> and its cooldown.
/// </para>
///
/// <para>
/// The team arms of SetPlayerModel are not ported, on purpose: stock reaches them only
/// with teamplay on, because its deathmatch forces every join to team 0, and here a
/// deathmatch player may stand on a real team (a divergence <see cref="ITeams"/> names).
/// There they would ignore the model the player asked for. Teamplay is SetPlayerTeamModel.
/// </para>
/// </summary>
public sealed class PlayerModels(IConnectedClients clients, IEngineEntities entities, IEngineEntityRender render,
                                 IEnginePlayers players, IEnginePrecache precache, IGameClock clock, IChat chat,
                                 ITeams teams, ConVarRegistry convars, IHooks<IPlayerModelOwner>? owners = null)
    : IGameLifecycle, ITeamChangeWatcher
{
    public const string ConVarName = "cl_playermodel";

    /// <summary>MODEL_CHANGE_INTERVAL.</summary>
    public const float ChangeInterval = 5f;

    /// <summary>What an invalid choice falls back to.</summary>
    public const string Fallback = "models/Combine_Soldier.mdl";

    /// <summary>TEAM_COMBINE.</summary>
    public const int TeamCombine = 2;

    /// <summary>TEAM_REBELS.</summary>
    public const int TeamRebels = 3;

    private const string HumanModels = "models/human";

    /// <summary>g_ppszRandomCitizenModels, in its order: the teamplay rotation walks it.</summary>
    public static IReadOnlyList<string> Citizens { get; } = new[]
    {
        "male_01", "male_02", "female_01", "male_03", "female_02", "male_04", "female_03", "male_05",
        "female_04", "male_06", "female_06", "male_07", "female_07", "male_08", "male_09",
    }.Select(name => $"models/humans/group03/{name}.mdl").ToArray();

    /// <summary>g_ppszRandomCombineModels.</summary>
    public static IReadOnlyList<string> Combine { get; } =
    [
        "models/combine_soldier.mdl",
        "models/combine_soldier_prisonguard.mdl",
        "models/combine_super_soldier.mdl",
        "models/police.mdl",
    ];

    private readonly TeamPlayConVar? _teamplay = convars.Get<TeamPlayConVar>();

    private readonly Dictionary<EntityHandle, Worn> _worn = new();

    private int _lastCitizen;
    private int _lastCombine;

    private readonly record struct Worn(string Model, float NextChange);

    /// <summary>Whether this game's players wear HL2MP's models at all.</summary>
    private bool Applies => owners is not { All.Count: > 0 };

    private bool Teamplay => _teamplay?.Value == true;

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.PlayerModels;

    /// <summary>CHL2MP_Player::Precache: both lists, because any player may ask for any of them.</summary>
    void IGameLifecycle.OnLevelInitPreEntity(string mapName)
    {
        if (!Applies)
            return;

        foreach (string model in Citizens)
            precache.PrecacheModel(model);
        foreach (string model in Combine)
            precache.PrecacheModel(model);
    }

    void IGameLifecycle.OnLevelShutdown() => _worn.Clear();

    /// <summary>ValidatePlayerModel.</summary>
    public static bool IsValid(string? model)
        => model is not null
           && (Citizens.Contains(model, StringComparer.OrdinalIgnoreCase)
               || Combine.Contains(model, StringComparer.OrdinalIgnoreCase));

    /// <summary>The model <paramref name="player"/> spawns in, or null when the mod says what its players wear.</summary>
    public string? ForSpawn(EntityHandle player)
    {
        if (!Applies)
            return null;

        return _worn.TryGetValue(player, out var worn) ? worn.Model : Choose(player);
    }

    /// <summary>The player has left: the next occupant of the slot chooses for themselves.</summary>
    public void Forget(EntityHandle player) => _worn.Remove(player);

    /// <summary>CHL2MPRules::ClientSettingsChanged's model half.</summary>
    public void SettingsChanged(int entIndex)
    {
        if (!Applies)
            return;

        EntityHandle player = players.GetHandle(entIndex);

        // Not spawned yet: the first spawn reads the convar as it then stands.
        if (!_worn.TryGetValue(player, out var worn))
            return;

        string wanted = clients.ClientConVar(entIndex, ConVarName) ?? string.Empty;
        if (string.Equals(wanted, worn.Model, StringComparison.OrdinalIgnoreCase))
            return;

        // Too soon: set the cvar back, which calls this again with the models matching.
        if (worn.NextChange >= clock.Now)
        {
            clients.ClientCommand(entIndex, $"{ConVarName} {worn.Model}\n");
            chat.Print(entIndex, $"Please wait {(int)(worn.NextChange - clock.Now)} more seconds before trying to switch.\n");
            return;
        }

        if (Teamplay)
        {
            // The move lands in TeamChanged, which dresses them for the new side.
            teams.Join(player, wanted.Contains(HumanModels, StringComparison.OrdinalIgnoreCase) ? TeamRebels : TeamCombine);
            return;
        }

        chat.Print(entIndex, $"Your player model is: {Wear(player)}\n");
    }

    /// <summary>CHL2MP_Player::ChangeTeam's tail.</summary>
    public void TeamChanged(EntityHandle player, int oldTeam, int newTeam)
    {
        if (Applies && _worn.ContainsKey(player))
            Wear(player);
    }

    private string Wear(EntityHandle player)
    {
        string model = Choose(player);
        render.SetModel(player, model);
        return model;
    }

    private string Choose(EntityHandle player)
    {
        string model = Teamplay ? TeamModel(player) : FreeModel(player);
        _worn[player] = new Worn(model, clock.Now + ChangeInterval);
        return model;
    }

    /// <summary>SetPlayerModel: what they asked for, else what they wear, else the fallback; a refusal is written back.</summary>
    private string FreeModel(EntityHandle player)
    {
        int index = entities.GetIndex(player);
        string? wanted = clients.ClientConVar(index, ConVarName);
        if (IsValid(wanted))
            return wanted!;

        string current = _worn.TryGetValue(player, out var worn) && IsValid(worn.Model) ? worn.Model : Fallback;
        clients.ClientCommand(index, $"{ConVarName} {current}\n");
        return current;
    }

    /// <summary>SetPlayerTeamModel: what they asked for if it suits their side, else the side's next model.</summary>
    private string TeamModel(EntityHandle player)
    {
        int index = entities.GetIndex(player);
        string? wanted = clients.ClientConVar(index, ConVarName);
        if (!IsValid(wanted))
        {
            wanted = Fallback;
            clients.ClientCommand(index, $"{ConVarName} {wanted}\n");
        }

        bool human = wanted!.Contains(HumanModels, StringComparison.OrdinalIgnoreCase);
        int team = players.GetTeam(player);
        if (team == TeamCombine && human)
        {
            _lastCombine = (_lastCombine + 1) % Combine.Count;
            return Combine[_lastCombine];
        }

        if (team == TeamRebels && !human)
        {
            _lastCitizen = (_lastCitizen + 1) % Citizens.Count;
            return Citizens[_lastCitizen];
        }

        return wanted;
    }
}
