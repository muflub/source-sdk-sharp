using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Trees;

/// <summary>
/// §10's facts: each skill's observable, through the trees, moves with its
/// level (the other nine held at the Medium preset), never against it, and
/// apart at 0 and 10. A curve flattened in the code turns its fact red
/// (tools/ai-mutations flat-&lt;skill&gt;).
/// </summary>
public sealed class SkillObservableTests
{
    private static Duel At(string skill, int level, NavGraph? graph = null, params ItemSpawn[] items)
    {
        SkillProfile p = Duel.Preset("normal");
        return new Duel(p.With(p.IndexOf(skill), level), graph, items);
    }

    private static void Monotonic(string skill, bool rises, Func<int, double> observe)
    {
        double[] o = [.. Enumerable.Range(0, 11).Select(observe)];
        string all = string.Join(", ", o.Select(v => v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));
        for (int l = 1; l < o.Length; l++)
            Assert.True(rises ? o[l] >= o[l - 1] : o[l] <= o[l - 1], $"{skill}'s observable goes against its level at {l}: {all}");
        Assert.True(rises ? o[10] > o[0] : o[10] < o[0], $"{skill}'s observable does not move: {all}");
    }

    private static readonly ItemSpawn[] Far =
    [
        new(0, "item_battery", ItemKind.Armour, new AiVector(3000, 0, 0), 30f),
        new(1, "item_battery", ItemKind.Armour, new AiVector(-3000, 0, 0), 30f),
    ];

    /// <summary>Accuracy: the mean angle between the bot's aim and the target's chest falls.</summary>
    [Fact]
    public void AccuracyNarrowsTheAim() => Monotonic("accuracy", rises: false, level =>
    {
        var d = At("accuracy", level);
        var at = new AiVector(600f, 0f, 0f);
        d.Step(d.Self(), [(Duel.Opponent, at)], d.SeeAt(Duel.Opponent, at));
        double sum = 0;
        for (int i = 0; i < 300; i++)
        {
            IntentCommand c = d.Step(d.Self(), [(Duel.Opponent, at)]);
            AiVector aim = c.Look.Point - new AiVector(0f, 0f, 64f), chest = at + new AiVector(0f, 0f, -16f);
            sum += Math.Acos(Math.Clamp(aim.Normalised().Dot(chest.Normalised()), -1f, 1f));
        }

        return sum / 300;
    });

    /// <summary>Alertness: glances off the path while roaming rise.</summary>
    [Fact]
    public void AlertnessGlancesMore() => Monotonic("alertness", rises: true, level =>
    {
        var d = At("alertness", level, null, Far);
        for (int i = 0; i < 20000; i++)
            d.Step(d.Self(), []);
        return d.Decider.Glances;
    });

    /// <summary>Aggression: the nearest distance at which the bot closes on a sighted opponent falls (it engages closer).</summary>
    [Fact]
    public void AggressionEngagesCloser() => Monotonic("aggression", rises: false, level =>
    {
        for (float dist = 100f; dist <= 3000f; dist += 25f)
        {
            var d = At("aggression", level);
            var at = new AiVector(dist, 0f, 0f);
            d.Step(d.Self(), [(Duel.Opponent, at)], d.SeeAt(Duel.Opponent, at));
            if (d.Step(d.Self(), [(Duel.Opponent, at)]).Move.Kind == MoveKind.Follow)
                return dist;
        }

        return 3000;
    });

    /// <summary>Cover: the clips at which the bot reloads in a lull (nobody in sight) rise.</summary>
    [Fact]
    public void CoverReloadsInALullSooner() => Monotonic("cover", rises: true, level =>
    {
        int reloads = 0;
        for (int clip = 1; clip < 45; clip++)
        {
            var d = At("cover", level, null, Far);
            if (d.Step(d.Self(clip: clip), []).Action.Kind == ActionKind.Reload)
                reloads++;
        }

        return reloads;
    });

    /// <summary>Tenacity: decides spent searching after a sound rise.</summary>
    [Fact]
    public void TenacitySearchesLonger() => Monotonic("tenacity", rises: true, level =>
    {
        var d = At("tenacity", level, null, Far);
        d.Step(d.Self(), [], AiEvent.SoundHeard(Duel.Bot, new Tick(6), SoundKind.Gunfire, new AiVector(4000f, 4000f, 0f), 1f, ActorId.None, Duel.Smg1));
        int searching = 0;
        for (int i = 0; i < 6000; i++)
        {
            d.Step(d.Self(), []);
            if (d.Decider.Doing.Branch == "Search")
                searching++;
        }

        return searching;
    });

    /// <summary>Cunning: decides spent waiting in ambush over an opponent's camp spot rise.</summary>
    [Fact]
    public void CunningAmbushesMore() => Monotonic("cunning", rises: true, level =>
    {
        string[] rows = [.. Enumerable.Repeat(new string('.', 40), 40)];
        var (g, ids) = Grids.Parse(rows);
        var d = At("cunning", level, g, Far);
        d.Step(d.Self(), [], d.SeeAt(Duel.Opponent, new AiVector(600f, 600f, 0f)));
        d.Step(d.Self(), [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, new AiVector(600f, 600f, 0f), AiVector.Zero));
        d.Memory.HabitsOf(Duel.Opponent).Camped(Grids.At(ids, 20, 20));
        int ambush = 0;
        for (int i = 0; i < 2000; i++)
        {
            d.Step(d.Self(), []);
            if (d.Decider.Doing.Branch == "Ambush")
                ambush++;
        }

        return ambush;
    });

    /// <summary>The map's sight for the cover facts: every cell at or beyond <paramref name="edge"/> on x is out of the opponent's sight.</summary>
    private sealed class HiddenBeyond(float edge, NavGraph graph) : ICellSight
    {
        public float Range => 4096f;
        public bool Visible(int a, int b) => graph.Cell(b).Centre.X < edge;
    }

    /// <summary>
    /// Cover's §10 observable (<c>coverWeight</c>): a bot reloading in sight of
    /// its opponent goes for a place the opponent cannot see, over cover at a
    /// range of distances; how far it goes for one rises, so the share of the
    /// layouts in which it ends unseen rises.
    /// </summary>
    [Fact]
    public void CoverSeeksUnseenPlacesFarther() => Monotonic("cover", rises: true, level =>
    {
        var (g, _) = Grids.Parse([.. Enumerable.Repeat(new string('.', 30), 3)]);
        int unseen = 0;
        for (float edge = 64f; edge <= 640f; edge += 32f)
        {
            SkillProfile p = Duel.Preset("normal");
            var d = new Duel(p.With(p.IndexOf("cover"), level), g, new HiddenBeyond(edge, g), Far);
            var at = new AiVector(300f, 48f, 0f);
            d.Step(d.Self(), [(Duel.Opponent, at)], d.SeeAt(Duel.Opponent, at));
            IntentCommand c = d.Step(d.Self(extra: SelfFlags.Reloading), [(Duel.Opponent, at)]);
            if (d.Decider.Doing.Verdict == "retreat (own reload)" && c.Move.Kind == MoveKind.Retreat && c.Move.Corner0.X >= edge)
                unseen++;
        }

        return unseen;
    });

    /// <summary>
    /// Cunning's §10 observable (<c>routeAltWeight</c>): the share of
    /// approaches to an opponent lost from sight that go by a flank, not the
    /// shortest way, rises.
    /// </summary>
    [Fact]
    public void CunningTakesTheLongWayMore() => Monotonic("cunning", rises: true, level =>
    {
        var (g, _) = Grids.Parse([.. Enumerable.Repeat(new string('.', 40), 40)]);
        var d = At("cunning", level, g, Far);
        var at = new AiVector(1000f, 1000f, 0f);
        for (int i = 0; i < 300; i++)
        {
            d.Step(d.Self(), [(Duel.Opponent, at)], d.SeeAt(Duel.Opponent, at));
            d.Step(d.Self(), [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, at, AiVector.Zero));
            d.Step(d.Self(), []);
        }

        Assert.True(d.Decider.Approaches >= 250, $"the approaches were begun: {d.Decider.Approaches}");
        return d.Decider.Flanks / (double)d.Decider.Approaches;
    });

    /// <summary>Discipline: pushes on an opponent seen to empty its clip, over a range of distances, rise.</summary>
    [Fact]
    public void DisciplinePushesOnTheEstimateMore() => Monotonic("discipline", rises: true, level =>
    {
        int pushes = 0;
        for (float dist = 50f; dist <= 400f; dist += 10f)
        {
            var d = At("discipline", level);
            var at = new AiVector(dist, 0f, 0f);
            d.Step(d.Self(), [(Duel.Opponent, at)], d.SeeAt(Duel.Opponent, at));
            d.Step(d.Self(), [(Duel.Opponent, at)], d.Shots(Duel.Opponent, 45));
            if (d.Decider.Doing.Verdict == "push")
                pushes++;
        }

        return pushes;
    });

    /// <summary>Agility: the share of a fight's decides spent strafing rises.</summary>
    [Fact]
    public void AgilityStrafesMore() => Monotonic("agility", rises: true, level =>
    {
        var d = At("agility", level);
        var at = new AiVector(380f, 0f, 0f);
        d.Step(d.Self(), [(Duel.Opponent, at)], d.SeeAt(Duel.Opponent, at));
        int strafes = 0;
        for (int i = 0; i < 3000; i++)
        {
            IntentCommand c = d.Step(d.Self(), [(Duel.Opponent, at)]);
            if (c.Move.Kind == MoveKind.Strafe && MathF.Abs(c.Move.Direction.Y) > 0.5f)
                strafes++;
        }

        return strafes / 3000.0;
    });

    /// <summary>Nerve: the healths at which the bot retreats from an opponent in sight fall.</summary>
    [Fact]
    public void NerveRetreatsLess() => Monotonic("nerve", rises: false, level =>
    {
        int retreats = 0;
        var at = new AiVector(300f, 0f, 0f);
        for (int health = 1; health <= 100; health++)
        {
            var d = At("nerve", level);
            d.Step(d.Self(health: health), [(Duel.Opponent, at)], d.SeeAt(Duel.Opponent, at));
            if (d.Decider.Doing.Branch == "Survive")
                retreats++;
        }

        return retreats;
    });

    /// <summary>Headhunter: the share of pairs in which the bot picks the scoreboard's leader over a nearer low scorer rises.</summary>
    [Fact]
    public void HeadhunterGoesForTheLeaderMore() => Monotonic("headhunter", rises: true, level =>
    {
        int leader = 0, pairs = 0;
        for (float far = 400f; far <= 2000f; far += 200f)
        {
            for (float near = 100f; near < far; near += 200f)
            {
                var d = At("headhunter", level);
                var leaderAt = new AiVector(far, 0f, 0f);
                var lowAt = new AiVector(0f, near, 0f);
                d.Step(d.Self(), [(Duel.Opponent, leaderAt), (Duel.Other, lowAt)],
                       [.. d.SeeAt(Duel.Opponent, leaderAt), .. d.SeeAt(Duel.Other, lowAt), d.Row(Duel.Opponent, 9, 1), d.Row(Duel.Other, 0, 3)]);
                pairs++;
                if (d.Decider.Doing.Target == Duel.Opponent)
                    leader++;
            }
        }

        return leader / (double)pairs;
    });
}
