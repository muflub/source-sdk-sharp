using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ISoundEmitHook"/> (plan_hl2mp_ai §4.2): a hook sees each sound
/// with the recipient set the engine was handed, after PAS, attenuation and
/// the prediction rules, by the script name it was emitted under.
/// </summary>
public sealed class SoundEmitHookTests
{
    private sealed class Heard : ISoundEmitHook
    {
        public readonly List<(string Name, int Owner, int Level, string Recipients, Vector Origin)> All = new();

        public void OnEmitted(in EmittedSound sound)
            => All.Add((sound.SoundName, sound.Owner, sound.SoundLevel, string.Join(",", sound.Recipients.ToArray()), sound.Origin));
    }

    private static readonly EntityHandle Shooter = new(2u | (1u << 16));

    [Fact]
    public void TheHookSeesTheFinalRecipientsWithoutThePredictingShooter()
    {
        var engine = new SoundEmitterTests.FakeEngine();
        engine.Scripts["Weapon_SMG1.Single"] = new SoundEmitterTests.Script
        {
            Index = 9, Channel = 1, Volume = 1f, SoundLevel = 140, Pitch = 100, Wave = "weapons/smg1/smg1_fire1.wav",
            Waves = ["weapons/smg1/smg1_fire1.wav"],
        };
        engine.PasBits[0] = 0b1110;   // players 2, 3 and 4 are in the PAS
        var listeners = new SoundEmitterTests.Listeners();
        listeners.Ears[2] = new Vector(0, 0, 0);
        listeners.Ears[3] = new Vector(0, 0, 0);
        listeners.Ears[4] = new Vector(0, 0, 0);
        var entities = new RecordingEngine().Returns("Entities.IsValid", true)
                                            .Returns("Entities.GetIndex", 2)
                                            .Returns("Entities.GetWorldSpaceCenter", new Vector(10, 20, 30));
        var heard = new Heard();
        var emitter = new SoundEmitter(engine, new SoundRecipients(engine, listeners), new SoundEmitterTests.Captions(),
                                       new RecordingEngine(), _ => null, () => false, _ => { }, _ => { },
                                       () => [heard], index => index == 2 ? new Vector(10, 20, 30) : null);
        var sounds = new ServerSounds(emitter, entities, _ => { }, _ => { });
        var prediction = new PredictionSuppression(new PredictionSuppressionTests.Clients());

        using (prediction.Suppress(2))
            sounds.EmitPredicted(Shooter, "Weapon_SMG1.Single", prediction);

        Assert.Equal([("Weapon_SMG1.Single", 2, 140, "3,4", new Vector(10, 20, 30))], heard.All);
        Assert.Equal(engine.Emits.Single().Recipients, heard.All.Single().Recipients);
    }

    [Fact]
    public void WithNoHookNothingIsAsked()
    {
        var engine = new SoundEmitterTests.FakeEngine();
        engine.Scripts["Weapon_SMG1.Single"] = new SoundEmitterTests.Script
        {
            Index = 9, Channel = 1, Volume = 1f, SoundLevel = 140, Pitch = 100, Wave = "w.wav", Waves = ["w.wav"],
        };
        int asked = 0;
        var emitter = new SoundEmitter(engine, new SoundRecipients(engine, new SoundEmitterTests.Listeners()), new SoundEmitterTests.Captions(),
                                       new RecordingEngine(), _ => null, () => false, _ => { }, _ => { },
                                       () => { asked++; return []; }, _ => throw new InvalidOperationException("no origin is asked without a hook"));
        emitter.Emit(new RecipientFilter(8), 0, "Weapon_SMG1.Single");
        Assert.Equal(1, asked);
        Assert.Single(engine.Emits);
    }
}
