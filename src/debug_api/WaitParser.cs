using System.Globalization;

namespace Debug.Api;

/// <summary>
/// A <c>/wait</c> query, read into one condition and its budget.
///
/// <para>
/// A CLOSED SET OF CONDITIONS, NOT AN EXPRESSION LANGUAGE. Each is one
/// question the gate actually asks; exactly one may be named per request, and
/// naming none or two is an error -- a wait that silently picked one would
/// answer a different question than the caller asked.
/// </para>
/// </summary>
public static class WaitParser
{
    private static readonly IReadOnlyList<string> Kinds = ["ticks", "field", "exists", "gone", "event", "settled", "census", "io"];

    public static bool TryParse(Func<string, string?> get, long eventsHead,
                                out WaitCondition? condition, out int timeoutTicks, out TimeSpan wall,
                                out string? error)
    {
        condition = null;
        timeoutTicks = DebugApiServer.DefaultWaitTicks;
        wall = default;
        error = null;

        string[] named = Kinds.Where(k => !string.IsNullOrEmpty(get(k))).ToArray();
        if (named.Length != 1)
        {
            error = named.Length == 0
                ? "name one condition: " + string.Join(", ", Kinds)
                : "name one condition, not " + string.Join(" and ", named);
            return false;
        }

        if (get("timeout") is { Length: > 0 } timeoutText)
        {
            if (!int.TryParse(timeoutText, out timeoutTicks) || timeoutTicks < 0 || timeoutTicks > DebugApiServer.MaxWaitTicks)
            {
                error = $"timeout is a count of server ticks, 0 to {DebugApiServer.MaxWaitTicks}";
                return false;
            }
        }

        wall = DebugApiServer.DefaultWall(timeoutTicks);
        if (get("wall") is { Length: > 0 } wallText)
        {
            if (!double.TryParse(wallText, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                || seconds <= 0 || seconds > 600)
            {
                error = "wall is a wall-clock cap in seconds, above 0 and at most 600";
                return false;
            }

            wall = TimeSpan.FromSeconds(seconds);
        }

        condition = named[0] switch
        {
            "ticks" => Ticks(get, ref timeoutTicks, out error),
            "field" => Field(get, out error),
            "exists" => Exists(get, out error),
            "gone" => Gone(get, out error),
            "event" => Event(get, eventsHead, out error),
            "settled" => Settled(get, out error),
            "census" => Census(get, out error),
            "io" => Io(get, out error),
            _ => null,
        };

        return condition is not null;
    }

    private static WaitCondition? Ticks(Func<string, string?> get, ref int timeoutTicks, out string? error)
    {
        error = null;
        if (!int.TryParse(get("ticks"), out int ticks) || ticks < 0 || ticks > DebugApiServer.MaxWaitTicks)
        {
            error = $"ticks is a count of server ticks, 0 to {DebugApiServer.MaxWaitTicks}";
            return null;
        }

        // Waiting N ticks IS the condition: its budget is the count, and an
        // explicit timeout shorter than it would make it fail by design.
        if (get("timeout") is not { Length: > 0 })
            timeoutTicks = ticks;

        if (timeoutTicks < ticks)
        {
            error = $"a timeout of {timeoutTicks} tick(s) can never see {ticks} pass";
            return null;
        }

        return new TicksCondition(ticks);
    }

    private static EntitySelector? Selector(Func<string, string?> get, out string? error)
    {
        var selector = EntitySelector.Parse(get, out error);
        if (selector is null && error is null)
            error = "name the entity: index, handle or name";
        return selector;
    }

    private static WaitCondition? Field(Func<string, string?> get, out string? error)
    {
        var selector = Selector(get, out error);
        if (selector is null)
            return null;

        if (!WaitCompare.TryParse(get("op"), out WaitOp op))
        {
            error = $"no such op '{get("op")}'; expected eq, ne, lt, le, gt, ge or changed";
            return null;
        }

        string? value = get("value");
        if (op != WaitOp.Changed && value is null)
        {
            error = "a comparison needs value=";
            return null;
        }

        double eps = 0.001;
        if (get("eps") is { Length: > 0 } epsText
            && !double.TryParse(epsText, NumberStyles.Float, CultureInfo.InvariantCulture, out eps))
        {
            error = "eps is a number";
            return null;
        }

        return new FieldCondition(selector, get("field")!, op, value, eps);
    }

    private static WaitCondition? Exists(Func<string, string?> get, out string? error)
    {
        string? className = get("class");
        var selector = EntitySelector.Parse(get, out error);
        if (error is not null)
            return null;

        if (selector is null == string.IsNullOrEmpty(className))
        {
            error = "exists needs one of: index, handle, name or class";
            return null;
        }

        return new ExistsCondition(selector, className);
    }

    private static WaitCondition? Gone(Func<string, string?> get, out string? error)
    {
        var selector = Selector(get, out error);
        return selector is null ? null : new GoneCondition(selector);
    }

    private static WaitCondition? Event(Func<string, string?> get, long eventsHead, out string? error)
    {
        string kind = get("event")!;

        // The kind rides on event= itself; "*" or "any" is every kind.
        string? Filtered(string key) => key == "kind" ? (kind is "*" or "any" ? null : kind) : get(key);

        if (!EventFilter.TryParse(Filtered, out var filter, out error))
            return null;

        long since = eventsHead;
        if (get("since") is { Length: > 0 } sinceText && !long.TryParse(sinceText, out since))
        {
            error = "since is an event sequence";
            return null;
        }

        return new EventCondition(filter, since);
    }

    private static WaitCondition? Settled(Func<string, string?> get, out string? error)
    {
        var selector = Selector(get, out error);
        if (selector is null)
            return null;

        double eps = 1.0;
        if (get("eps") is { Length: > 0 } epsText
            && (!double.TryParse(epsText, NumberStyles.Float, CultureInfo.InvariantCulture, out eps) || eps < 0))
        {
            error = "eps is a speed in units per second";
            return null;
        }

        int forTicks = 5;
        if (get("for") is { Length: > 0 } forText && (!int.TryParse(forText, out forTicks) || forTicks < 1))
        {
            error = "for is a count of consecutive ticks, 1 or more";
            return null;
        }

        bool asleepOnly = get("asleep") is { Length: > 0 } a && a != "0";
        return new SettledCondition(selector, eps, forTicks, asleepOnly);
    }

    private static WaitCondition? Census(Func<string, string?> get, out string? error)
    {
        error = null;
        if (!int.TryParse(get("census"), out int count) || count < 0)
        {
            error = "census is a count of entities";
            return null;
        }

        string? opText = get("op");
        WaitOp op = WaitOp.Ge;
        if (!string.IsNullOrEmpty(opText) && (!WaitCompare.TryParse(opText, out op) || op == WaitOp.Changed))
        {
            error = $"no such op '{opText}' for a census; expected eq, ne, lt, le, gt or ge";
            return null;
        }

        string className = get("class") is { Length: > 0 } c ? c : "*";
        return new CensusCondition(className, op, count);
    }

    private static WaitCondition? Io(Func<string, string?> get, out string? error)
    {
        error = null;
        if (get("io") != "empty")
        {
            error = "io=empty is the one I/O condition";
            return null;
        }

        return new IoEmptyCondition();
    }
}
