using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's prediction filtering against stock's: the suppress host
/// (IPredictionSystem::SuppressHostEvents), CRecipientFilter::UsePredictionRules
/// and CTempEntsSystem::SuppressTE, and CBasePlayer::IsPredictingWeapons over
/// the client's userinfo.
/// </summary>
public class PredictionSuppressionTests
{
    internal sealed class Clients : IClientInfo
    {
        public readonly Dictionary<(int, string), string?> ConVars = [];

        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => null;
        public float? AvgLatency(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => ConVars.GetValueOrDefault((entIndex, name));

        public Clients Predicting(int player, string predict = "1", string weapons = "1")
        {
            ConVars[(player, "cl_predict")] = predict;
            ConVars[(player, "cl_predictweapons")] = weapons;
            return this;
        }
    }

    private static RecipientFilter Of(params int[] players)
    {
        var filter = new RecipientFilter(8);
        foreach (int p in players)
            filter.Add(p);
        return filter;
    }

    private static int[] Slots(RecipientFilter filter) => filter.Slots.ToArray();

    [Fact]
    public void A_player_predicts_weapons_only_with_cl_predict_and_cl_predictweapons_on()
    {
        var clients = new Clients().Predicting(1).Predicting(2, weapons: "0").Predicting(3, predict: "0");
        var prediction = new PredictionSuppression(clients);

        Assert.True(prediction.PredictsWeapons(1));
        Assert.False(prediction.PredictsWeapons(2), "cl_predictweapons 0");
        Assert.False(prediction.PredictsWeapons(3), "cl_predict 0");
        Assert.False(prediction.PredictsWeapons(4), "a client that sent neither: Q_atoi(\"\") is 0");
    }

    [Fact]
    public void The_host_is_the_suppressed_player_until_the_window_closes()
    {
        var prediction = new PredictionSuppression(new Clients());

        using (prediction.Suppress(2))
        {
            Assert.Equal(2, prediction.Host);
            using (prediction.Suppress(5))
                Assert.Equal(5, prediction.Host);
            Assert.Equal(2, prediction.Host);
        }

        Assert.Equal(0, prediction.Host);
    }

    [Fact]
    public void Prediction_rules_take_the_host_out_and_keep_it_out()
    {
        var prediction = new PredictionSuppression(new Clients());
        var filter = Of(1, 2, 3);

        using (prediction.Suppress(2))
            prediction.UsePredictionRules(filter);

        Assert.Equal([1, 3], Slots(filter));

        filter.Add(2);
        Assert.Equal([1, 3], Slots(filter));
        Assert.Equal([1, 3], Slots(filter.Copy()));
    }

    [Fact]
    public void Prediction_rules_with_no_host_change_nothing()
    {
        var filter = Of(1, 2);
        new PredictionSuppression(new Clients()).UsePredictionRules(filter);

        Assert.Equal([1, 2], Slots(filter));
    }

    [Fact]
    public void A_temp_entity_in_the_window_skips_the_host_and_is_not_sent_to_nobody()
    {
        var prediction = new PredictionSuppression(new Clients());

        Assert.False(prediction.SuppressTempEntity(Of(2), ignorePredictionCull: false), "no host: sent as is");

        using (prediction.Suppress(2))
        {
            var others = Of(1, 2);
            Assert.False(prediction.SuppressTempEntity(others, ignorePredictionCull: false));
            Assert.Equal([1], Slots(others));

            Assert.True(prediction.SuppressTempEntity(Of(2), ignorePredictionCull: false),
                        "the host alone: SuppressTE answers true and nothing is sent");

            var kept = Of(2);
            Assert.False(prediction.SuppressTempEntity(kept, ignorePredictionCull: true));
            Assert.Equal([2], Slots(kept));
        }
    }

    //-------------------------------------------------------------------------
    // The two consumers.
    //-------------------------------------------------------------------------

    private static readonly EntityHandle Shooter = new(2u | (1u << 16));

    [Fact]
    public void A_weapon_sound_reaches_everyone_in_earshot_but_the_predicting_shooter()
    {
        var engine = new SoundEmitterTests.FakeEngine();
        engine.Scripts["Weapon_Pistol.Single"] = new SoundEmitterTests.Script
        {
            Index = 9, Channel = 1, Volume = 1f, SoundLevel = 140, Pitch = 100, Wave = "weapons/pistol/pistol_fire2.wav",
            Waves = ["weapons/pistol/pistol_fire2.wav"],
        };
        engine.PasBits[0] = 0b110;   // players 2 and 3
        var listeners = new SoundEmitterTests.Listeners();
        listeners.Ears[2] = new Vector(0, 0, 0);
        listeners.Ears[3] = new Vector(0, 0, 0);
        var entities = new RecordingEngine().Returns("Entities.IsValid", true)
                                            .Returns("Entities.GetIndex", 2)
                                            .Returns("Entities.GetWorldSpaceCenter", new Vector(0, 0, 0));
        var emitter = new SoundEmitter(engine, new SoundRecipients(engine, listeners), new SoundEmitterTests.Captions(),
                                       new RecordingEngine(), _ => null, () => false, _ => { }, _ => { });
        var sounds = new ServerSounds(emitter, entities, _ => { }, _ => { });
        var prediction = new PredictionSuppression(new Clients());

        using (prediction.Suppress(2))
            sounds.EmitPredicted(Shooter, "Weapon_Pistol.Single", prediction);
        sounds.EmitPredicted(Shooter, "Weapon_Pistol.Single", prediction);

        Assert.Equal(["3", "2,3"], engine.Emits.Select(e => e.Recipients));
        Assert.All(engine.Emits, e => Assert.Equal(2, e.EntIndex));
    }
}
