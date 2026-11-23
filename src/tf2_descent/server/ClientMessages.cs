using TF2.Descent.Shared;
using SourceSharp.Abi;

namespace TF2.Descent.Server;

/// <summary>Sends TF2.Descent.Shared's messages to one client (by entity index) or to all (0).</summary>
[SideService(Side.Server)]
public sealed class ClientMessages(IUserMessageSender messages)
{
    private readonly List<(int EntIndex, string Text, string Arg, int Level)> _afterArrival = [];
    private int _level;

    private readonly Dictionary<int, (string Text, string Arg)> _wayTold = [];
    private int _way = -1;
    private bool _sameStay;

    /// <summary>A line on the player's HUD: plain text, or a <c>#Token</c> whose <c>%s1</c> the client fills with <paramref name="arg"/>.</summary>
    public void Notice(int entIndex, string text, string arg = "")
    {
        if (entIndex == _way)
        {
            if (_sameStay && _wayTold.TryGetValue(entIndex, out var told) && told == (text, arg))
                return;
            _wayTold[entIndex] = (text, arg);
        }
        messages.Send(new Shared.Notice { Text = text, Arg = arg }, entIndex);
    }

    /// <summary>
    /// A way down or up asks for <paramref name="entIndex"/> (<see cref="WayStays"/>): until
    /// <see cref="EndWay"/>, a notice to him that repeats the last one a way told him in this
    /// <paramref name="sameStay"/> is not sent again (the levels' cage buttons and walked ways ask
    /// every 3 seconds while he stands in them); a new stay, or another reason, is told.
    /// </summary>
    public void BeginWay(int entIndex, bool sameStay)
    {
        _way = entIndex;
        _sameStay = sameStay;
    }

    public void EndWay() => _way = -1;

    /// <summary>
    /// A notice for after the level change this process is about to make (the whole run's F28:
    /// unhosted, a notice sent just before a <c>changelevel</c> never reached the HUD): held, and
    /// sent by <see cref="Hero.ArrivalNotices"/> once the player stands on the next level.
    /// </summary>
    public void NoticeAfterArrival(int entIndex, string text, string arg = "")
        => _afterArrival.Add((entIndex, text, arg, _level));

    /// <summary>A level began: the notices held before it are now due on it.</summary>
    public void LevelBegan() => _level++;

    /// <summary>Whether a notice held for after a level change is due on this level.</summary>
    public bool ArrivalNoticesDue => _afterArrival.Exists(n => n.Level < _level);

    /// <summary>Sends the player's notices held for after a level change that are due now; answers how many.</summary>
    public int SendArrivalNotices(int entIndex)
    {
        int sent = 0;
        for (int i = 0; i < _afterArrival.Count; i++)
        {
            var n = _afterArrival[i];
            if (n.EntIndex != entIndex || n.Level >= _level)
                continue;
            Notice(n.EntIndex, n.Text, n.Arg);
            _afterArrival.RemoveAt(i--);
            sent++;
        }
        return sent;
    }

    /// <summary>
    /// Tells clients the next level's map and its download URL; false when it
    /// does not fit a message or <paramref name="sha256"/> is neither empty nor
    /// a SHA-256.
    /// </summary>
    public bool NextLevel(int entIndex, int depth, string map, string url, ReadOnlySpan<byte> sha256 = default)
    {
        if (sha256.Length is not (0 or Shared.NextLevel.HashBytes))
            return false;

        return messages.Send(new Shared.NextLevel
        {
            Depth = Shared.Instance.DepthOf(depth),
            Map = map,
            Url = url,
            Sha256 = sha256.ToArray(),
        }, entIndex);
    }

    /// <summary>Asks the client for a fresh Steam ticket; false when the nonce cannot travel back in a reply.</summary>
    public bool Reauth(int entIndex, string nonce, ulong gameServerSteamId)
        => ReauthMessage.Of(nonce, gameServerSteamId) is { } payload && messages.Send(payload, entIndex);

    /// <summary>
    /// IMMUNE over the robot (UI6): the player's hit on the robot body in <paramref name="robotEntity"/>
    /// did nothing, for its immunity to damage type <paramref name="damageType"/> (damage_types.json's
    /// row; -1 for one the table lacks).
    /// </summary>
    public void RobotImmune(int entIndex, int robotEntity, int damageType)
        => messages.Send(new Shared.RobotImmune
        {
            Entity = (short)Math.Clamp(robotEntity, 0, short.MaxValue),
            DamageType = damageType is >= 0 and < 255 ? (byte)damageType : (byte)255,
        }, entIndex);

    public void Instance(int entIndex, string kind, int depth)
        => messages.Send(new Shared.Instance { Kind = kind, Depth = Shared.Instance.DepthOf(depth) }, entIndex);
}
