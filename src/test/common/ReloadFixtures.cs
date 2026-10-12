using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// An enum used as a map key, which must number every member explicitly
/// (SSVAR012) because a map key is a number a mapper typed against an FGD.
/// </summary>
public enum ReloadMood
{
    Calm  = 0,
    Cross = 1,
    Livid = 2,
}

/// <summary>
/// A flags enum, which is the case that separates saving from displaying: a
/// value with several bits set formats as <c>Cutting|Burning</c>, and nothing
/// reads that back.
/// </summary>
[Flags]
public enum ReloadHazard
{
    None    = 0,
    Cutting = 1,
    Burning = 2,
    Loud    = 4,
}

/// <summary>The map keys the reload fixture declares.</summary>
[ScriptVars]
public partial class ReloadVars
{
    public float Speed { get; set; } = 100f;

    public string Label { get; set; } = "stock";

    public Vector Where { get; set; } = new(1f, 1f, 1f);

    public ReloadMood Mood { get; set; } = ReloadMood.Calm;

    public ReloadHazard Hazard { get; set; } = ReloadHazard.None;
}

/// <summary>
/// An entity with a map key set, two outputs and one plain field that is
/// neither — which is the only half a default cannot discover.
/// </summary>
[ScriptedEntity("reload_fixture")]
public sealed class ReloadFixtureEntity : ScriptedEntity
{
    [ScriptVars] public ReloadVars Vars = new();

    public readonly Output OnFirst = new();
    public readonly Output OnSecond = new();

    /// <summary>Neither networked nor a map key: the reason SaveState is virtual.</summary>
    public int TravelledLegs;

    /// <summary>How many times the map's I/O reached this entity.</summary>
    public int Nudges;

    [ScriptedEvent] public void Nudge() => Nudges++;

    public override void SaveState(SavedEntity state)
    {
        base.SaveState(state);
        state.Set("legs", TravelledLegs);
    }

    public override void LoadState(SavedEntity state)
    {
        base.LoadState(state);
        TravelledLegs = state.GetInt("legs");
    }
}

/// <summary>
/// The mistake the mechanism has to survive: an override that does its own
/// thing and never chains to <c>base</c>.
/// </summary>
[ScriptedEntity("reload_forgetful")]
public sealed class ReloadForgetfulEntity : ScriptedEntity
{
    [ScriptVars] public ReloadVars Vars = new();

    public int Counter;

    public override void SaveState(SavedEntity state) => state.Set("counter", Counter);

    public override void LoadState(SavedEntity state) => Counter = state.GetInt("counter");
}
