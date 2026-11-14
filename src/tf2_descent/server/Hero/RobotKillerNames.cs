using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Mode;
using TF2.Server.Combat;

namespace TF2.Descent.Server.Hero;

/// <summary>Which robot template an entity is the body of (a robot's hit names its body as the attacker).</summary>
public interface IRobotTemplateOf
{
    /// <summary>The robots.json template of the robot whose body <paramref name="entity"/> is; null for anything else.</summary>
    string? TemplateOf(EntityHandle entity);
}

/// <summary>The horde's bodies: a lent body names its roster row, and the row its template.</summary>
[SideService(Side.Server, As = [typeof(IRobotTemplateOf)])]
public sealed class HordeTemplateOf(IKillRoster horde, IEntityDirectory directory) : IRobotTemplateOf
{
    public string? TemplateOf(EntityHandle entity)
    {
        if (entity == EntityHandle.None || directory.Get(entity) is not RobotBody body || horde.Roster is not { } roster)
            return null;
        var id = body.Robot;
        return id.IsValid && id.Index < roster.Count && roster.Id(id.Index) == id ? roster.Template(id.Index) : null;
    }
}

/// <summary>
/// The kill feed names the robot that killed a hero (WO6, the death notice's
/// <see cref="ITFKillerNames"/>): its template's token, <c>#Descent_Robot_&lt;template&gt;</c>
/// (the language file's), in the robots' team colour (<see cref="DescentMode.RobotTeam"/>,
/// BLU). A robot's hit names its body; a hero who bleeds out is named the
/// robot that put him down (<see cref="Downed"/>). Any other death (a fall, a
/// suicide) is not this member's.
/// </summary>
[SideService(Side.Server)]
public sealed class RobotKillerNames(IDescentModeState mode, IRobotTemplateOf robots, IEngineEntities entities)
    : ITFKillerNames, IGameLifecycle, IOrderedHook
{
    /// <summary>The token of a robot template's name: this plus the template id.</summary>
    public const string Prefix = "#Descent_Robot_";

    /// <summary>In TF2 Descent's downed block.</summary>
    public const int Order = ModStages.First + 956;

    public int HookOrder => Order;

    public int LifecycleOrder => Order;

    private readonly Dictionary<int, string> _downedBy = new();

    /// <summary>The hero in <paramref name="playerIndex"/> went down to a hit by <paramref name="attacker"/>: the robot it is, if one, is his killer should he bleed out.</summary>
    public void Downed(int playerIndex, EntityHandle attacker)
    {
        if (robots.TemplateOf(attacker) is { } template)
            _downedBy[playerIndex] = template;
        else
            _downedBy.Remove(playerIndex);
    }

    public bool TryToken(in DeathNoticeFacts death, out string token, out int team)
    {
        (token, team) = ("", 0);
        if (!mode.Active || death.Victim == EntityHandle.None)
            return false;
        int index = entities.GetIndex(death.Victim);
        string? template = robots.TemplateOf(death.Attacker);
        if (template is null && death.Attacker == EntityHandle.None)
            template = _downedBy.GetValueOrDefault(index);
        _downedBy.Remove(index);
        if (template is null)
            return false;
        token = Prefix + template;
        team = (int)DescentMode.RobotTeam;
        return true;
    }

    public void OnLevelInit(string mapName) => _downedBy.Clear();
}
