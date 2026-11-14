using SourceSharp.Abi;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using TF2.Descent.Client.Bestiary;
using TF2.Descent.Client.Content;
using TF2.Descent.Client.Robots;
using TF2.Descent.Shared;

namespace TF2.Descent.Client.Ui;

//=============================================================================
// THE ROBOT BARS' SOURCE (plan_descent_logic UI6). Which robots get a bar, and
// what each shows, from what this client holds: the bodies' own net state
// (anyone may see it: name, the life fraction, champion, statuses) and the
// local hero's knowledge (what the server let him see: his count, the life
// numbers, resistances, affixes, style; IMMUNE his hit drew). The design names
// a bar and its parts, not which robots show one; the choice here:
//   - the robot under the crosshair, within CrosshairRange, not behind a wall;
//   - a robot whose bar fell in the last RecentSeconds, on screen, within
//     RecentRange (a party's fight shows too: the client cannot tell whose
//     hit it was, and nothing on the wire says);
//   - a robot whose IMMUNE is fresh (ImmuneSeconds).
// Never a parked, dead or dormant body. At most MaxBars, nearest first.
//=============================================================================

/// <summary>One robot body as the client holds it: its entity, where it stands, and its net state's public parts.</summary>
public readonly record struct RobotOnClient(int Entity, Vector Origin, float Scale, int Template, byte Life, RobotFlags Flags, byte Statuses);

/// <summary>The robot bodies this client holds, lent, alive and not dormant.</summary>
public interface IClientRobotBodies
{
    /// <summary>Fills <paramref name="into"/> (cleared first).</summary>
    void Live(List<RobotOnClient> into);
}

/// <summary><see cref="IClientRobotBodies"/> over the live <c>tf2_descent_robot</c> entities.</summary>
[SideService(Side.Client, As = [typeof(IClientRobotBodies)])]
public sealed class EngineClientRobotBodies(ClientRobotPoses robots, IEngineEntities entities) : IClientRobotBodies
{
    public void Live(List<RobotOnClient> into)
    {
        into.Clear();
        foreach (var r in robots.Live)
        {
            if (r.Dormant || (r.Flags & RobotFlags.Lent) == 0 || (r.Flags & RobotFlags.Dead) != 0 || r.State.Template == 0)
                continue;
            into.Add(new RobotOnClient((int)r.Handle.EntryIndex, entities.GetOrigin(r.Handle), r.State.ModelScale > 0 ? r.State.ModelScale : 1f,
                                       r.State.Template, r.State.Life, r.Flags, r.State.Statuses));
        }
    }
}

/// <summary>What the bars ask of the view: where a place falls on the screen, where the player stands, and whether a line of sight is clear.</summary>
public interface IRobotBarView
{
    Vector LocalOrigin { get; }

    /// <summary>A world place as fractions of the screen; false when it is behind the view.</summary>
    bool ToScreen(Vector world, out float x, out float y);

    /// <summary>Whether nothing of the world stands between <paramref name="from"/> and <paramref name="to"/>.</summary>
    bool Clear(Vector from, Vector to);
}

/// <summary><see cref="IRobotBarView"/> over the core's marker projection and the client's world trace.</summary>
[SideService(Side.Client, As = [typeof(IRobotBarView)])]
public sealed class EngineRobotBarView(IMarkerView view, IEngineWorldQuery world) : IRobotBarView
{
    public Vector LocalOrigin => view.LocalOrigin;

    public bool ToScreen(Vector at, out float x, out float y) => view.ToScreen(at, out x, out y);

    public bool Clear(Vector from, Vector to) => world.ClientTraceLine(from, to, TraceMask.Brushes).Fraction >= 1f;
}

/// <summary>The robot bars (<see cref="IDescentRobotBars"/>), chosen and filled once a client frame.</summary>
[SideService(Side.Client, As = [typeof(IDescentRobotBars)])]
public sealed class RobotBarsSource(IClientRobotBodies bodies, IRobotKnowledge knowledge, DescentCatalog catalog, IRobotBarView view,
                                    IGameClock clock) : IDescentRobotBars
{
    public const int MaxBars = 6;
    public const float CrosshairRange = 2000f, RecentRange = 3000f, RecentSeconds = 5f, ImmuneSeconds = 2f;

    /// <summary>A robot of scale 1 stands this tall (TF2's player hull).</summary>
    public const float HullHeight = 82f;

    /// <summary>The plate's foot stands this far over the head.</summary>
    public const float OverHead = 12f;

    /// <summary>The player's eye over his origin, for the line of sight.</summary>
    public const float EyeHeight = 64f;

    private readonly List<RobotOnClient> _live = new();
    private readonly Dictionary<int, (byte Life, float FellAt)> _seen = new();
    private readonly List<int> _stale = new();
    private readonly DescentRobotBar[] _bars = new DescentRobotBar[MaxBars];
    private readonly RobotView?[] _views = new RobotView?[MaxBars];
    private readonly byte[] _statuses = new byte[MaxBars];
    private readonly (int Index, float Distance)[] _picked = new (int, float)[MaxBars];
    private string[] _typeTokens = [];
    private string[] _statusTokens = [];
    private int _count;
    private float _builtAt = float.NaN;

    private void Build()
    {
        float now = clock.Now;
        if (now == _builtAt)
            return;
        // A new level's clock starts again: what was seen fell on the last one.
        if (now < _builtAt)
            _seen.Clear();
        _builtAt = now;
        Tokens();

        bodies.Live(_live);
        Vector here = view.LocalOrigin;
        Vector eye = new(here.X, here.Y, here.Z + EyeHeight);
        int picked = 0;
        for (int i = 0; i < _live.Count; i++)
        {
            var r = _live[i];
            float distance = (r.Origin - here).Length;
            bool fell = Fell(r, now);
            bool immune = knowledge.ImmuneOf(r.Entity) is { } hit && now - hit.At >= 0 && now - hit.At <= ImmuneSeconds;
            bool show = immune || (fell && distance <= RecentRange && OnScreen(r));
            if (!show && distance <= CrosshairRange && UnderCrosshair(r))
                show = view.Clear(eye, new Vector(r.Origin.X, r.Origin.Y, r.Origin.Z + HullHeight * r.Scale * 0.6f));
            if (show)
                picked = Pick(picked, i, distance);
        }
        Forget();

        _count = 0;
        for (int p = 0; p < picked; p++)
            Fill(_live[_picked[p].Index], now);
    }

    /// <summary>Keeps the nearest <see cref="MaxBars"/>, nearest first.</summary>
    private int Pick(int picked, int index, float distance)
    {
        int at = picked;
        while (at > 0 && _picked[at - 1].Distance > distance)
            at--;
        if (at >= MaxBars)
            return picked;
        int last = Math.Min(picked, MaxBars - 1);
        for (int k = last; k > at; k--)
            _picked[k] = _picked[k - 1];
        _picked[at] = (index, distance);
        return Math.Min(picked + 1, MaxBars);
    }

    /// <summary>Whether the body's bar fell within the last <see cref="RecentSeconds"/> (remembered per entity).</summary>
    private bool Fell(in RobotOnClient r, float now)
    {
        float fellAt = float.NegativeInfinity;
        if (_seen.TryGetValue(r.Entity, out var last))
        {
            fellAt = r.Life < last.Life ? now : r.Life > last.Life ? float.NegativeInfinity : last.FellAt;
        }
        _seen[r.Entity] = (r.Life, fellAt);
        return now - fellAt <= RecentSeconds;
    }

    /// <summary>Drops the remembered bars of bodies this client no longer holds.</summary>
    private void Forget()
    {
        if (_seen.Count <= _live.Count)
            return;
        _stale.Clear();
        foreach (int entity in _seen.Keys)
        {
            bool held = false;
            foreach (var r in _live)
                held |= r.Entity == entity;
            if (!held)
                _stale.Add(entity);
        }
        foreach (int entity in _stale)
            _seen.Remove(entity);
    }

    private bool OnScreen(in RobotOnClient r) =>
        view.ToScreen(Head(r), out float x, out float y) && x is >= 0f and <= 1f && y is >= 0f and <= 1f;

    /// <summary>The middle of the screen falls inside the body's box on screen: its height feet to head, its width half that.</summary>
    private bool UnderCrosshair(in RobotOnClient r)
    {
        if (!view.ToScreen(r.Origin, out float fx, out float fy)
            || !view.ToScreen(new Vector(r.Origin.X, r.Origin.Y, r.Origin.Z + HullHeight * r.Scale), out float hx, out float hy))
            return false;
        float top = Math.Min(fy, hy), bottom = Math.Max(fy, hy);
        float half = (bottom - top) * 0.25f;
        float cx = (fx + hx) / 2;
        return top <= 0.5f && bottom >= 0.5f && Math.Abs(cx - 0.5f) <= half;
    }

    private static Vector Head(in RobotOnClient r) => new(r.Origin.X, r.Origin.Y, r.Origin.Z + HullHeight * r.Scale + OverHead);

    private void Fill(in RobotOnClient r, float now)
    {
        view.ToScreen(Head(r), out float x, out float y);
        int row = r.Template - 1;
        string name = DescentCatalog.At(catalog.Robots, row)?.Name ?? "";
        var known = knowledge.ViewOf(r.Entity) is { } v && v.Template == r.Template && !knowledge.ContentMismatch ? v : null;
        int kills = knowledge.KillsOf(row);
        string immuneToken = "";
        bool immune = false;
        if (knowledge.ImmuneOf(r.Entity) is { } hit && now - hit.At >= 0 && now - hit.At <= ImmuneSeconds)
        {
            immune = true;
            immuneToken = hit.DamageType >= 0 && hit.DamageType < _typeTokens.Length ? _typeTokens[hit.DamageType] : "";
        }

        _views[_count] = known;
        _statuses[_count] = r.Statuses;
        _bars[_count++] = new DescentRobotBar(
            x, y, name, r.Life / 255f, kills > 0 ? kills : -1,
            known?.Life ?? 0, known?.MaxLife ?? 0, (r.Flags & RobotFlags.Champion) != 0,
            known?.Style ?? "", immune, immuneToken);
    }

    private void Tokens()
    {
        if (_typeTokens.Length != catalog.DamageTypes.Count)
            _typeTokens = [.. catalog.DamageTypes.Select(t => "#Descent_Type_" + t.Name)];
        if (_statusTokens.Length != catalog.Statuses.Count)
            _statusTokens = [.. catalog.Statuses.Select(s => "#Descent_Status_" + s)];
    }

    public int BarCount
    {
        get
        {
            Build();
            return _count;
        }
    }

    public DescentRobotBar Bar(int i) => _bars[i];

    public int ResistCount(int bar) => _views[bar]?.Resist is { } r ? Math.Min(r.Count, _typeTokens.Length) : 0;

    public string ResistToken(int bar, int r) => _typeTokens[r];

    public int ResistPercent(int bar, int r) => _views[bar]?.Resist is { } list && r < list.Count ? list[r] : 0;

    public bool ResistImmune(int bar, int r) => _views[bar]?.Immunity == r;

    public int AffixCount(int bar) => _views[bar]?.Affixes?.Count ?? 0;

    public string AffixName(int bar, int a) =>
        _views[bar]?.Affixes is { } list && a < list.Count ? DescentCatalog.At(catalog.Champions, list[a])?.Name ?? "" : "";

    public int StatusCount(int bar)
    {
        int n = 0;
        for (int s = 0; s < _statusTokens.Length && s < 8; s++)
            if ((_statuses[bar] & (1 << s)) != 0)
                n++;
        return n;
    }

    public string StatusToken(int bar, int s)
    {
        for (int k = 0; k < _statusTokens.Length && k < 8; k++)
        {
            if ((_statuses[bar] & (1 << k)) == 0)
                continue;
            if (s-- == 0)
                return _statusTokens[k];
        }
        return "";
    }
}

/// <summary><c>tf2_descent_bars_cl</c>: the robot bars this frame and what each shows, then what the hero knows of every robot this client holds (UI6's live proof).</summary>
[ConCommand("tf2_descent_bars_cl", Help = "tf2_descent_bars_cl: the robot bars shown now, and the robot views and counts this client holds")]
public sealed class RobotBarsClientCommand(IDescentRobotBars bars, IRobotKnowledge knowledge, DescentCatalog catalog, ILog log)
{
    public void Execute()
    {
        var sb = new System.Text.StringBuilder();
        int n = bars.BarCount;
        sb.Append(FormattableString.Invariant($"[TF2Descent/Bars] {n} bars\n"));
        for (int i = 0; i < n; i++)
        {
            var b = bars.Bar(i);
            sb.Append(FormattableString.Invariant(
                $"[TF2Descent/Bars]   {b.Name} at ({b.X:0.00}, {b.Y:0.00}) life {b.LifeFraction:0.00} kills {b.Kills} numbers {b.Life}/{b.LifeMax} champion {b.Champion} style '{b.Style}' immune {b.Immune} {b.ImmuneToken}"));
            for (int r = 0; r < bars.ResistCount(i); r++)
                sb.Append(FormattableString.Invariant($" {bars.ResistToken(i, r)}={(bars.ResistImmune(i, r) ? "IMMUNE" : bars.ResistPercent(i, r).ToString(System.Globalization.CultureInfo.InvariantCulture))}"));
            for (int a = 0; a < bars.AffixCount(i); a++)
                sb.Append(" affix ").Append(bars.AffixName(i, a));
            for (int s = 0; s < bars.StatusCount(i); s++)
                sb.Append(' ').Append(bars.StatusToken(i, s));
            sb.Append('\n');
        }
        foreach (var (entity, v) in knowledge.Views.OrderBy(kv => kv.Key))
            sb.Append(FormattableString.Invariant(
                $"[TF2Descent/Bars]   view #{entity} {DescentCatalog.At(catalog.Robots, v.Template - 1)?.Id} reveal {v.Reveal} kills {knowledge.KillsOf(v.Template - 1)} life {v.Life}/{v.MaxLife} resist {(v.Resist is null ? "hidden" : string.Join(",", v.Resist))} immune {v.Immunity} affixes {(v.Affixes is null ? "hidden" : string.Join(",", v.Affixes))} style '{v.Style}'\n"));
        log.Msg(sb.ToString());
    }
}
