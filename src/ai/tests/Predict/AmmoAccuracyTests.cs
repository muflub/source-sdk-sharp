using Xunit;

namespace SourceSharp.Ai.Tests.Predict;

/// <summary>
/// The ammo estimate and the push test against the truth (plan_hl2mp_ai
/// §5.3.4, §8.2) over a seeded synthetic duel the test reads and the
/// estimate cannot: an SMG1 opponent fires bursts and reloads by its own
/// style; the bot hears most shots and every reload while in hearing, and
/// nothing out of it. Numbers: the range holds the true clip at least 80 %
/// of the ticks in hearing and is at most half a clip wide at least 60 % of
/// them (the ignore-ammo baseline is never that narrow); the push fires
/// inside the true reload window at least 70 % of the times it fires,
/// against at most 35 % for a push on health alone.
/// </summary>
public sealed class AmmoAccuracyTests
{
    private const float Dt = 0.015f;
    private static readonly WeaponFacts Smg1 = new(3, "weapon_smg1", 45, 0.075f, 1.5f, true, false, 0f);

    private sealed record Run(int InHearing, int Contains, int Narrow, int BaselineNarrow, int Pushes, int PushesInWindow,
                              int HealthPushes, int HealthPushesInWindow);

    private static Run Duel(ulong seed)
    {
        var rng = new AiRandom(seed);
        float Uniform() => (rng.NextUInt64() % 10_000) / 10_000f;
        var est = new AmmoEstimate();
        est.SeeWeapon(Smg1, new Tick(0));
        est.ReloadStarted(Smg1, new Tick(0), Dt);
        int clip = 0, burstLeft = 0, nextShot = 0, reloadEnd = VisionParams.Ticks(1.5f, Dt), pauseUntil = 0, health = 100;
        bool reloading = true, hearing = true;
        int hearingSwitch = 400;
        int inHearing = 0, contains = 0, narrow = 0, pushes = 0, inWindow = 0, hp = 0, hpIn = 0;
        for (int t = 1; t < 66 * 120; t++)
        {
            var tick = new Tick(t);
            if (t >= hearingSwitch)
            {
                hearing = !hearing;
                hearingSwitch = t + (hearing ? 400 + (int)(Uniform() * 400) : 60 + (int)(Uniform() * 120));
            }

            // The truth: reload when due, fire bursts with pauses, reload by style.
            if (reloading && t >= reloadEnd)
            {
                reloading = false;
                clip = 45;
            }

            if (!reloading && t >= pauseUntil && burstLeft == 0)
                burstLeft = 3 + (int)(Uniform() * 8);
            if (!reloading && burstLeft > 0 && t >= nextShot && clip > 0)
            {
                clip--;
                burstLeft--;
                nextShot = t + VisionParams.Ticks(Smg1.CadenceSeconds, Dt);
                if (hearing && Uniform() < 0.95f)
                    est.Shot(Smg1, 1, tick);
                if (burstLeft == 0)
                    pauseUntil = t + 20 + (int)(Uniform() * 80);
            }

            if (!reloading && (clip == 0 || (burstLeft == 0 && clip < 8 && Uniform() < 0.05f)))
            {
                reloading = true;
                reloadEnd = t + VisionParams.Ticks(Smg1.ReloadSeconds, Dt);
                burstLeft = 0;
                if (hearing)
                    est.ReloadStarted(Smg1, tick, Dt);
            }

            if (Uniform() < 0.004f)
                health = health > 50 ? 30 : 100;
            est.Advance(Smg1, tick, Dt, hearing);

            if (hearing)
            {
                inHearing++;
                if (clip >= est.Min && clip <= est.Max)
                    contains++;
                if (est.Width <= 45 / 2)
                    narrow++;
            }

            if (t % 6 == 0)
            {
                AmmoWindow.Verdict v = AmmoWindow.Judge(est, Smg1, tick, Dt, 1f, 300f, 320f, 0.6f, 0.2f);
                if (v.Push)
                {
                    pushes++;
                    if (reloading)
                        inWindow++;
                }

                if (health < 50)
                {
                    hp++;
                    if (reloading)
                        hpIn++;
                }
            }
        }

        return new Run(inHearing, contains, narrow, 0, pushes, inWindow, hp, hpIn);
    }

    private static readonly Lazy<Run> Result = new(() => Duel(42));

    [Fact]
    public void TheRangeHoldsTheTrueClipMostOfTheTimeInHearing()
    {
        Run r = Result.Value;
        Assert.True(r.InHearing > 1000, "the control: the duel was heard");
        float share = r.Contains / (float)r.InHearing;
        Assert.True(share >= 0.80f, $"contains the truth {share:P1} of {r.InHearing} ticks");
    }

    [Fact]
    public void TheRangeIsNarrowWhereTheIgnoreAmmoBaselineNeverIs()
    {
        Run r = Result.Value;
        float share = r.Narrow / (float)r.InHearing;
        Assert.True(share >= 0.60f, $"at most half a clip wide {share:P1} of the ticks");
        Assert.Equal(0, r.BaselineNarrow);   // [0, clip] is never half a clip wide
    }

    [Fact]
    public void ThePushFiresInsideTheTrueReloadWindowAndHealthAloneDoesNot()
    {
        Run r = Result.Value;
        Assert.True(r.Pushes > 10 && r.HealthPushes > 10, $"the control: both pushed ({r.Pushes}, {r.HealthPushes})");
        float ours = r.PushesInWindow / (float)r.Pushes, health = r.HealthPushesInWindow / (float)r.HealthPushes;
        Assert.True(ours >= 0.70f, $"inside the window {ours:P1} of {r.Pushes} pushes");
        Assert.True(health <= 0.35f, $"health alone {health:P1} of {r.HealthPushes}");
    }
}
