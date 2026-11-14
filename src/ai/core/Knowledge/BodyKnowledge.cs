using System.Collections.Immutable;

namespace SourceSharp.Ai;

/// <summary>
/// A class of player as someone who knows the game knows it (plan_bots_teams_tf2_part_g G.2.1):
/// how fast it runs. The binding's data: a game with classes says one row a class, a game
/// without says one for everybody.
/// </summary>
/// <param name="RunSpeed">Its run, units a second: how fast an actor of the class out of sight is believed to move.</param>
public readonly record struct BodyFacts(float RunSpeed);

/// <summary>
/// The game's bodies by the class a client draws an actor as (<see cref="ActorFacts.Class"/>).
/// A class with no row, and 0 (a game without classes, or an actor not yet seen), is
/// <see cref="Unknown"/>'s.
/// </summary>
public sealed class BodyKnowledge
{
    /// <summary>
    /// The run believed of anyone when the game's knowledge names no bodies: the 190 u/s every
    /// recording before the bodies was made and replays with (a recording's world carries none).
    /// </summary>
    public const float DefaultRunSpeed = 190f;

    private readonly BodyFacts?[] _byClass;

    public BodyKnowledge(BodyFacts unknown, IEnumerable<(int Class, BodyFacts Facts)> byClass)
    {
        ArgumentNullException.ThrowIfNull(byClass);
        Unknown = unknown;
        Classes = [.. byClass];
        int max = Classes.Length == 0 ? 0 : Classes.Max(c => c.Class) + 1;
        _byClass = new BodyFacts?[max];
        foreach ((int cls, BodyFacts facts) in Classes)
        {
            if (cls <= 0)
                throw new ArgumentException($"class {cls} is no class: 0 is the unknown body's", nameof(byClass));
            _byClass[cls] = facts;
        }
    }

    /// <summary>The body of an actor whose class has no row.</summary>
    public BodyFacts Unknown { get; }

    /// <summary>The rows, as given.</summary>
    public ImmutableArray<(int Class, BodyFacts Facts)> Classes { get; }

    /// <summary>The body of <paramref name="playerClass"/>, or <see cref="Unknown"/>'s.</summary>
    public BodyFacts Of(int playerClass)
        => (uint)playerClass < (uint)_byClass.Length && _byClass[playerClass] is { } f ? f : Unknown;

    /// <summary>One body for everybody: a game without classes.</summary>
    public static BodyKnowledge Same(BodyFacts everyone) => new(everyone, []);

    /// <summary>What a knowledge that names no bodies has: everybody runs at <see cref="DefaultRunSpeed"/>.</summary>
    public static BodyKnowledge Default() => Same(new BodyFacts(DefaultRunSpeed));
}
