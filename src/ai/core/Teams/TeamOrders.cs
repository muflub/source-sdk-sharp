namespace SourceSharp.Ai;

/// <summary>A human teammate's order in team chat (plan_bots_teams_tf2 B.5, Q5): a four-word vocabulary.</summary>
public enum OrderKind : byte
{
    None = 0,

    /// <summary>"follow me": follow at 200-400 u for followSeconds.</summary>
    Follow,

    /// <summary>"help": cover that human.</summary>
    Help,

    /// <summary>"go": the bots are released.</summary>
    Go,

    /// <summary>"hold": stay where it stands for followSeconds.</summary>
    Hold,
}

/// <summary>
/// Reads a team chat line for an order (Q5: "follow me", "help", "go", "hold"; two matches
/// per word list): the whole line, any case, its closing punctuation dropped. A line that
/// says more than an order is talk, not an order.
/// </summary>
public static class TeamOrders
{
    public static OrderKind Parse(string? text)
    {
        if (text is null)
            return OrderKind.None;
        string line = text.Trim().TrimEnd('!', '.', '?').Trim().ToLowerInvariant();
        return line switch
        {
            "follow me" or "on me" => OrderKind.Follow,
            "help" or "cover me" => OrderKind.Help,
            "go" or "go go" => OrderKind.Go,
            "hold" or "wait here" => OrderKind.Hold,
            _ => OrderKind.None,
        };
    }
}
