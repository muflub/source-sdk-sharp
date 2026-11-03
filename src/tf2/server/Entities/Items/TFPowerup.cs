//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace TF2.Server.Entities.Items;

/// <summary><c>powerupsize_t</c> (tf_powerup.h).</summary>
public enum PowerupSize
{
    Small = 0,
    Medium = 1,
    Full = 2,
}

/// <summary>
/// <c>CTFPowerup</c> (tf_powerup.cpp): what every TF map pickup shares over
/// the core's <see cref="PickupItem"/> — a size (<see cref="PackRatios(PowerupSize)"/>),
/// the map's on/off switch, the team it is for, a model the map may swap,
/// and the TF respawn time.
/// </summary>
public abstract class TFPowerup(ITFMapPlayers players, IEngineFiles files, IEngineWorldQuery world, PlacedObjects placed)
    : PickupItem(world, placed)
{
    /// <summary><c>PackRatios</c> (tf_powerup.cpp): small 0.2, medium 0.5, full 1.0.</summary>
    public static float PackRatios(PowerupSize size) => size switch
    {
        PowerupSize.Small => 0.2f,
        PowerupSize.Medium => 0.5f,
        _ => 1.0f,
    };

    /// <summary>
    /// <c>ITEM_RESPAWN_TIME</c> (tf_gamerules.cpp:155), what
    /// <c>CTFGameRules::FlItemRespawnTime</c> answers every item.
    /// </summary>
    public const float ItemRespawnTime = 10f;

    /// <summary>The players this item acts on.</summary>
    protected readonly ITFMapPlayers Players = players;

    [ScriptVars] public TFPowerupVars PowerupVars = new();

    [SaveField] private bool _disabled;
    [SaveField] private bool _respawning;

    /// <summary><c>GetPowerupSize</c>.</summary>
    public abstract PowerupSize Size { get; }

    /// <summary><c>PackRatios[GetPowerupSize()]</c>.</summary>
    public float PackRatio => PackRatios(Size);

    /// <summary><c>GetDefaultPowerupModel</c>.</summary>
    protected abstract string DefaultModel { get; }

    /// <summary><c>MyTouch</c>: hands the contents over, true when anything was given.</summary>
    protected abstract bool MyTouch(EntityHandle player);

    public bool IsDisabled => _disabled;

    /// <summary>Taken and not back yet (<c>m_bRespawning</c>).</summary>
    public bool IsRespawning => _respawning;

    /// <summary>
    /// <c>GetPowerupModel</c>: the map's <c>powerup_model</c> when that file
    /// exists, else the class's own.
    /// </summary>
    protected sealed override string Model
        => PowerupVars.PowerupModel.Length > 0 && files.GameFileSize(PowerupVars.PowerupModel) >= 0
            ? PowerupVars.PowerupModel
            : DefaultModel;

    /// <summary><c>GetRespawnDelay</c>: <see cref="ItemRespawnTime"/>.</summary>
    protected override float RespawnDelay => ItemRespawnTime;

    /// <summary>
    /// Not one of HL2MP's level-designer placed objects: CItem's
    /// AddLevelDesignerPlacedObject and its "AlyxEmp.Charge" are HL2MP's arms,
    /// and CTFPowerup's Materialize is its own.
    /// </summary>
    protected override bool JoinsPlacedObjects => false;

    public override void OnSpawn()
    {
        base.OnSpawn();
        _respawning = false;
        if (PowerupVars.StartDisabled)
            SetDisabled(true);
    }

    /// <summary>
    /// <c>ItemTouch</c>'s <c>MyTouch</c>, behind <see cref="ValidTouch"/> as
    /// every TF powerup's <c>MyTouch</c> asks it first.
    /// </summary>
    protected sealed override bool TryGive(EntityHandle player)
    {
        if (!ValidTouch(player) || !MyTouch(player))
            return false;

        _respawning = true;
        return true;
    }

    /// <summary>
    /// <c>CTFPowerup::ValidTouch</c>: enabled, a live player, and on the
    /// item's team when it has one. (The MvM invaders' arm is not a milestone
    /// mode.)
    /// </summary>
    protected virtual bool ValidTouch(EntityHandle player)
    {
        if (_disabled)
            return false;

        if (!Players.IsAlive(player))
            return false;

        int team = PowerupVars.TeamNum;
        return team == 0 || Players.Team(player) == team;
    }

    /// <summary>
    /// <c>Materialize</c>: back after the respawn time, unless the map set
    /// <c>AutoMaterialize</c> off, when only an Enable brings it back.
    /// </summary>
    public override void OnThink()
    {
        if (!PowerupVars.AutoMaterialize)
            return;

        Materialize();
    }

    private void Materialize()
    {
        _respawning = false;
        base.OnThink();
        if (_disabled)
            SetPickupEnabled(false);
    }

    [ScriptedEvent] private void OnEnable() => SetDisabled(false);

    [ScriptedEvent] private void OnDisable() => SetDisabled(true);

    [ScriptedEvent] private void OnToggle() => SetDisabled(!_disabled);

    /// <summary>
    /// <c>SetDisabled</c>: hidden while off; switched on it shows again
    /// unless it is still respawning, and one that waits for an Enable to
    /// materialize does so now.
    /// </summary>
    public void SetDisabled(bool disabled)
    {
        _disabled = disabled;
        if (disabled)
        {
            SetPickupEnabled(false);
        }
        else if (!_respawning)
        {
            SetPickupEnabled(true);
        }
        else if (!PowerupVars.AutoMaterialize)
        {
            Materialize();
        }
    }
}

/// <summary>The map keys <see cref="TFPowerup"/> declares (tf_powerup.cpp's datadesc, and CBaseEntity's TeamNum).</summary>
[ScriptVars]
public partial class TFPowerupVars
{
    public bool StartDisabled { get; set; }

    [VarName("powerup_model")] public string PowerupModel { get; set; } = string.Empty;

    public bool AutoMaterialize { get; set; } = true;

    /// <summary>CBaseEntity::KeyValue's TeamNum: the team the item is for, 0 for everyone.</summary>
    public int TeamNum { get; set; }
}
