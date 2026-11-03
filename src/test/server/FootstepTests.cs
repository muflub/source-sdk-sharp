using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A player's footsteps on the server: stock's CBasePlayer::UpdateStepSound
/// cadence and volumes, and CHL2MP_Player::PlayStepSound's server arm, which
/// sends the step to the PAS of the feet less the PVS (the players in the PVS
/// make the step client side).
/// </summary>
public sealed class FootstepTests
{
    private const float Tick = 0.015f;
    private const int Concrete = 'C';

    // ---------------------------------------------------------------- the rules

    private static List<(int Tick, float Volume)> Run(float speed, int flags = GameMovement.FlOnGround, int moveType = GameMovement.MoveTypeWalk,
                                                      int waterLevel = 0, int material = Concrete, bool footsteps = true, int ticks = 200)
    {
        var steps = new List<(int, float)>();
        float time = 0f;
        int skip = 0;
        for (int t = 0; t < ticks; t++)
        {
            float vol = StepSoundRules.Update(ref time, ref skip, Tick, footsteps, flags, moveType, waterLevel, speed, speed, material);
            if (vol > 0f)
                steps.Add((t, vol));
        }

        return steps;
    }

    [Fact]
    public void ARunnerStepsEveryThreeHundredMillisecondsAtHalfVolume()
    {
        List<(int Tick, float Volume)> steps = Run(250f);

        Assert.Equal(10, steps.Count);
        Assert.All(steps, s => Assert.Equal(0.5f, s.Volume));
        Assert.Equal(20, steps[1].Tick - steps[0].Tick);   // 300 ms of 15 ms ticks
    }

    [Fact]
    public void AWalkerStepsEveryFourHundredMillisecondsAtAFifth()
    {
        List<(int Tick, float Volume)> steps = Run(150f);

        Assert.All(steps, s => Assert.Equal(0.2f, s.Volume));
        Assert.Equal(27, steps[1].Tick - steps[0].Tick);   // 400 ms rounds up to 27 ticks
    }

    [Theory]
    [InlineData('D', 0.55f)]
    [InlineData('V', 0.7f)]
    [InlineData('M', 0.5f)]
    public void TheSurfaceSetsTheVolume(int material, float volume)
        => Assert.Equal(volume, Run(250f, material: material)[0].Volume);

    [Fact]
    public void ALadderClimberStepsEveryThreeHundredFiftyMillisecondsAtHalfVolume()
    {
        List<(int Tick, float Volume)> steps = Run(100f, flags: 0, moveType: GameMovement.MoveTypeLadder, material: -1);

        Assert.Equal(0.5f, steps[0].Volume);
        Assert.Equal(24, steps[1].Tick - steps[0].Tick);
    }

    [Fact]
    public void NoStepInTheAirTooSlowWithNoSurfaceOrWithFootstepsOff()
    {
        Assert.Empty(Run(250f, flags: 0));
        Assert.Empty(Run(80f));
        Assert.Empty(Run(250f, material: -1));
        Assert.Empty(Run(250f, footsteps: false));
        Assert.Empty(Run(250f, moveType: GameMovement.MoveTypeNoClip));
        Assert.Empty(Run(250f, flags: GameMovement.FlOnGround | GameMovement.FlFrozen));
        Assert.NotEmpty(Run(250f));
    }

    [Fact]
    public void AWaderSkipsTheFirstOfEveryFourSteps()
    {
        List<(int Tick, float Volume)> steps = Run(250f, waterLevel: GameMovement.WlWaist, ticks: 400);

        Assert.All(steps, s => Assert.Equal(0.65f, s.Volume));
        Assert.Equal(1, steps[0].Tick);   // the first is skipped, the next tick steps
        int[] gaps = steps.Zip(steps.Skip(1), (a, b) => b.Tick - a.Tick).ToArray();
        Assert.Equal(gaps[0] + 1, gaps[2]);   // every fourth waits a tick more: its step was skipped
    }

    [Fact]
    public void Hl2mpMakesNoStepForADuckingPlayerOrWithFootstepsOffInMultiplayer()
    {
        Assert.True(StepSoundRules.Hl2mpPlays(true, 8, GameMovement.FlOnGround));
        Assert.False(StepSoundRules.Hl2mpPlays(true, 8, GameMovement.FlOnGround | GameMovement.FlDucking));
        Assert.False(StepSoundRules.Hl2mpPlays(false, 8, GameMovement.FlOnGround));
        Assert.True(StepSoundRules.Hl2mpPlays(false, 1, GameMovement.FlOnGround));
    }

    [Theory]
    [InlineData("models/humans/group03/male_01.mdl", "NPC_Citizen")]
    [InlineData("models/police.mdl", "NPC_MetroPolice")]
    [InlineData("models/combine_soldier.mdl", "NPC_CombineS")]
    public void TheStepsPrefixIsTheModels(string model, string prefix)
        => Assert.Equal(prefix, StepSoundRules.Hl2mpSoundPrefix(model));

    /// <summary>
    /// CHL2MP_Player::Precache's PrecacheFootStepSounds: every model sound
    /// type's two run steps are declared, so the engine has the waves the step
    /// plays (live, an undeclared step said "not precached" and played nothing).
    /// </summary>
    [Fact]
    public void EveryModelsRunStepsArePrecached()
    {
        var declared = PrecacheRegistry.Declared(new RecordingLog(), TestServices.ServerGame)
            .Where(d => d.Kind == PrecacheKind.Sound).Select(d => d.Asset).ToHashSet();
        foreach (string prefix in new[] { "NPC_Citizen", "NPC_CombineS", "NPC_MetroPolice" })
        {
            Assert.Contains(StepSoundRules.Hl2mpStepName(prefix, true), declared);
            Assert.Contains(StepSoundRules.Hl2mpStepName(prefix, false), declared);
        }
    }

    // ---------------------------------------------------------------- the emit

    private readonly SoundEmitterTests.FakeEngine _engine = new();
    private readonly SoundEmitterTests.Listeners _listeners = new();

    private ServerSounds Sounds()
    {
        _engine.Scripts["NPC_Citizen.RunFootstepLeft"] = new SoundEmitterTests.Script
        {
            Index = 3, Channel = 0, Volume = 1f, SoundLevel = 70, Pitch = 100, Wave = "npc/footsteps/hardboot_generic1.wav",
            Waves = ["npc/footsteps/hardboot_generic1.wav"],
        };
        var emitter = new SoundEmitter(_engine, new SoundRecipients(_engine, _listeners), new SoundEmitterTests.Captions(),
                                       new RecordingEngine(), _ => null, () => false, _ => { }, _ => { });
        return new ServerSounds(emitter, new RecordingEngine(), _ => { }, _ => { }, null,
                                _ => "models/humans/group03/male_01.mdl", null, () => [_clientSteps]);
    }

    private sealed class ClientSteps : IClientStepHook
    {
        public readonly List<(int Owner, string Slots, int Level, string Name)> Seen = [];

        public void OnClientStep(in EmittedSound step)
            => Seen.Add((step.Owner, string.Join(",", step.Recipients.ToArray()), step.SoundLevel, step.SoundName));
    }

    private readonly ClientSteps _clientSteps = new();

    /// <summary>
    /// The players the server leaves out are told as the ones whose clients
    /// make the step themselves: the PVS's, the stepping player among them.
    /// </summary>
    [Fact]
    public void TheClientStepHookIsToldThePvssPlayers()
    {
        foreach (int slot in new[] { 1, 2, 3 })
            _listeners.Ears[slot] = default;
        _engine.PasBits[0] = 0b0111;
        _engine.PvsBits = new uint[IFwdEngineServer.PlayerBitWords];
        _engine.PvsBits[0] = 0b0011;

        Sounds().StepSound(1, new Vector(10, 20, 0), 0.5f, left: true);

        Assert.Equal((1, "1,2", 70, "npc/footsteps/hardboot_generic1.wav"), Assert.Single(_clientSteps.Seen));
    }

    [Fact]
    public void AStepGoesToThePasLessThePvsOnAMultiplayerServer()
    {
        foreach (int slot in new[] { 1, 2, 3 })
            _listeners.Ears[slot] = default;
        _engine.PasBits[0] = 0b0111;   // players 1, 2, 3 (bit n is player n + 1) hear the feet's area
        _engine.PvsBits = new uint[IFwdEngineServer.PlayerBitWords];
        _engine.PvsBits[0] = 0b0011;   // players 1 and 2 see them: their clients make the step

        Sounds().StepSound(1, new Vector(10, 20, 0), 0.5f, left: true);

        var emit = Assert.Single(_engine.Emits);
        Assert.Equal((1, "3", "npc/footsteps/hardboot_generic1.wav", 4, 0.5f, 70),
                     (emit.EntIndex, emit.Recipients, emit.Sample, emit.Channel, emit.Volume, emit.SoundLevel));
    }

    [Fact]
    public void AStepOnAOnePlayerServerGoesToThatPlayer()
    {
        _listeners.MaxClients = 1;
        _listeners.Ears[1] = default;

        Sounds().StepSound(1, new Vector(10, 20, 0), 0.5f, left: true);

        Assert.Equal("1", Assert.Single(_engine.Emits).Recipients);
    }

    // ---------------------------------------------------------------- the world

    private sealed class Steps : IMoveSoundSink
    {
        public readonly List<(int Ent, float Volume, bool Left)> Made = [];
        public bool Footsteps { get; set; } = true;
        public int MaxClients => 8;
        public void MoveSound(int entIndex, Vector origin, int channel, string sample, float volume, int soundLevel, int flags, int pitch) { }
        public void MoveSoundScript(int entIndex, Vector origin, string soundName) { }
        public void StepSound(int entIndex, Vector origin, float volume, bool left) => Made.Add((entIndex, volume, left));
    }

    private static List<(int Ent, float Volume, bool Left)> Walk(int flags, bool footsteps = true)
    {
        var steps = new Steps { Footsteps = footsteps };
        var world = new PlayerStepSounds();
        var host = new ServerPlayerMove();
        host.Move.Flags = flags;
        host.Move.MoveType = GameMovement.MoveTypeWalk;
        host.Move.FrameTime = Tick;
        for (int t = 0; t < 100; t++)
            world.Update(host, 2, new Vector(0, 0, 0), new Vector(250, 0, 0), Concrete, steps);
        return steps.Made;
    }

    [Fact]
    public void ARunningPlayerMakesStepsOnAlternateFeet()
    {
        List<(int Ent, float Volume, bool Left)> made = Walk(GameMovement.FlOnGround);

        Assert.Equal(5, made.Count);
        Assert.All(made, m => Assert.Equal((2, 0.5f), (m.Ent, m.Volume)));
        Assert.Equal([true, false, true, false, true], made.Select(m => m.Left));
    }

    [Fact]
    public void ADuckingPlayerOrFootstepsOffMakesNoStep()
    {
        Assert.Empty(Walk(GameMovement.FlOnGround | GameMovement.FlDucking));
        Assert.Empty(Walk(GameMovement.FlOnGround, footsteps: false));
    }
}
