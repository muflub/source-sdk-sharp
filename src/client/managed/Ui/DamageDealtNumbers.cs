using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The numbers the HUD shows for what the local player's hits did, from each
/// <c>player_hurt</c> they caused. Hits on one victim close together are one number
/// growing: a shotgun blast is its seven pellets and a burst its running total, where
/// a number per hit would be an unreadable stack.
/// </summary>
public sealed class DamageDealtNumbers(IGameClock clock, IKillFeedPlayers players) : IDamageDealtSource
{
    /// <summary>How long a number shows.</summary>
    public const float LifeSeconds = 1.2f;

    /// <summary>How close a hit must follow the last on that victim to join its number.</summary>
    public const float MergeSeconds = 0.15f;

    /// <summary>How many show at once, the oldest giving way.</summary>
    public const int MaxNumbers = 6;

    private readonly (int Victim, int Amount, float At)[] _numbers = new (int, int, float)[MaxNumbers];
    private int _count;

    public void Subscribe(PlayerHurtEvent hurts) => hurts.Fired += Add;

    public void Add(PlayerHurtArgs hurt)
    {
        if (hurt.DamageAmount <= 0 || hurt.AttackerUserId == hurt.UserId)
            return;

        if (!players.TryFind(hurt.AttackerUserId, out _, out _, out bool local) || !local)
            return;

        float now = clock.Now;
        if (_count > 0 && _numbers[_count - 1].Victim == hurt.UserId && now - _numbers[_count - 1].At <= MergeSeconds)
        {
            _numbers[_count - 1].Amount += hurt.DamageAmount;
            _numbers[_count - 1].At = now;
            return;
        }

        if (_count == MaxNumbers)
            RemoveOldest();

        _numbers[_count++] = (hurt.UserId, hurt.DamageAmount, now);
    }

    public int NumberCount
    {
        get
        {
            float now = clock.Now;

            // A level change takes the clock back, and a number from before it goes too.
            while (_count > 0 && (now - _numbers[0].At >= LifeSeconds || now < _numbers[0].At))
                RemoveOldest();

            return _count;
        }
    }

    public DamageNumber Number(int i)
        => new(_numbers[i].Amount, Math.Clamp((clock.Now - _numbers[i].At) / LifeSeconds, 0f, 1f));

    private void RemoveOldest()
    {
        Array.Copy(_numbers, 1, _numbers, 0, _count - 1);
        _count--;
    }
}
