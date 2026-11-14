namespace TF2.Descent.Rules.Hero;

/// <summary>What a contribution to Now comes from.</summary>
public enum SourceKind
{
    Item,
    Skill,
    Buff,
}

/// <summary>
/// Whether a source counts. An item whose requirement is unmet (C.3, ruling L2) is
/// <see cref="Inactive"/>; one worn to zero durability (2h) is <see cref="Broken"/>. Both
/// stay equipped and contribute nothing.
/// </summary>
public enum SourceState
{
    Active,
    Inactive,
    Broken,
}

/// <summary>A source of contributions: an equipped item, a skill rank, a buff.</summary>
public sealed record ContributionSource(SourceKind Kind, string Id, SourceState State = SourceState.Active)
{
    /// <summary>The one test of whether a source's contributions count: only an active source's do.</summary>
    public bool Counts => State == SourceState.Active;
}

/// <summary>
/// "+<paramref name="Amount"/> of <paramref name="Stat"/> from <paramref name="Source"/>".
/// The stat is a row of stats.json, an attribute included (gear that grants Brawn).
/// Items (RU3) and skills (RU6) feed the sheet through a list of these.
/// </summary>
public readonly record struct Contribution(string Stat, double Amount, ContributionSource Source);
