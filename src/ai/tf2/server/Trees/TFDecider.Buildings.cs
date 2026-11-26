using SourceSharp.Ai.TF2.Senses;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// A friendly building as the bot remembers it (round 2's S5, S7, S8): the binding's id for it, its kind, where it stood, its
/// builder's slot, its level, the flags its client drew on it (<see cref="Knowledge.TFBuildingFlags"/>) and when the bot last saw it.
/// </summary>
public readonly record struct KnownBuilding(int Id, TFThingType Type, AiVector At, int Owner, int Level, int Flags, Tick Last);

/// <summary>
/// The bot's own team's buildings, as it saw them (round 2's shared hooks): what its client drew of each friendly sentry,
/// dispenser and teleporter end in its view, kept with the tick it was last seen. Nothing here is told: a building the bot
/// never saw is not in it, and one it saw is where it saw it, as it was then (it may have been moved, sapped or lost since).
/// The ammo errand's dispensers (<see cref="AmmoPlace"/>), the sapped answer (TFDecider.Sapped.cs) and the teleporter's way
/// (TFDecider.Teleporter.cs) read it.
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>How many friendly buildings the bot remembers at once: four for each of three Engineers.</summary>
    public const int RememberedBuildings = 12;

    private readonly KnownBuilding[] _buildings = new KnownBuilding[RememberedBuildings];
    private int _buildingCount;

    /// <summary>The friendly buildings the bot remembers, in no order.</summary>
    public ReadOnlySpan<KnownBuilding> FriendlyBuildings => _buildings.AsSpan(0, _buildingCount);

    /// <summary>Reads the friendly buildings in this decide's view into the memory. Before the tree, once a decide.</summary>
    private void SeeBuildings()
    {
        foreach (ref readonly ThingFacts t in _view.Things)
        {
            if (t.Kind != ThingKind.Building || !TeamRule.IsGameTeam(t.Team) || t.Team != _self.Team)
                continue;
            KeepBuilding(new KnownBuilding(t.Id, (TFThingType)t.Type, t.Origin, t.OwnerSlot, t.Level, t.Flags, _now));
        }
    }

    /// <summary>Puts <paramref name="seen"/> in the memory: over its own row, else a free one, else the one seen longest ago.</summary>
    private void KeepBuilding(KnownBuilding seen)
    {
        int oldest = 0;
        for (int i = 0; i < _buildingCount; i++)
        {
            if (_buildings[i].Id == seen.Id)
            {
                _buildings[i] = seen;
                return;
            }

            if (_buildings[i].Last < _buildings[oldest].Last)
                oldest = i;
        }

        if (_buildingCount < _buildings.Length)
            _buildings[_buildingCount++] = seen;
        else
            _buildings[oldest] = seen;
    }

    /// <summary>The seconds since <paramref name="since"/>.</summary>
    private float SecondsSince(Tick since) => (_now - since) * _tick;

    /// <summary>
    /// The nearest friendly building of <paramref name="type"/> the bot saw within the last <paramref name="seconds"/>, not at
    /// <paramref name="skip"/> (when <paramref name="skipping"/>); false when it remembers none.
    /// </summary>
    private bool NearestFriendly(TFThingType type, float seconds, out KnownBuilding nearest, bool skipping = false, AiVector skip = default)
    {
        nearest = default;
        float best = float.MaxValue;
        foreach (KnownBuilding b in FriendlyBuildings)
        {
            if (b.Type != type || SecondsSince(b.Last) > seconds || (skipping && b.At.DistanceTo(skip) <= 1f))
                continue;
            float d = _self.Origin.DistanceTo(b.At);
            if (d < best)
                (best, nearest) = (d, b);
        }

        return best < float.MaxValue;
    }
}
