extern alias client;

using SourceSharp.Abi;

using Xunit;

using ClientEffects = client::SourceSharp.Abi.RecordingEngine;
using Disguise = client::TF2.Client.Player.TFDisguiseEffects;
using Hook = client::TF2.Client.Player.ITFClientConditionHook;
using ClientTFPlayerData = client::TF2.Client.Player.ClientTFPlayerData;
using ClientCarriers = client::TF2.Client.Player.TFClientCarriers;
using PlayerResources = client::TF2.Client.Player.TFClientPlayerResources;
using ClientDriver = client::TF2.Client.Player.TFClientConditionDriver;
using ClientWords = client::TF2.Shared.Player.TFPlayerNetStateViews;
using Mask = client::TF2.Shared.Conditions.TFCondMask;
using Sounds = client::SourceSharp.Client.Sounds.IClientEntitySounds;
using Cond = client::TF2.Shared.TFCond;
using TFTeam = client::TF2.Shared.TFTeam;

namespace TF2.Tests.Client;

/// <summary>
/// The Spy's disguise puff and sound, stock's <c>OnAddDisguising</c> and
/// <c>OnRemoveDisguised</c> client halves (tf_player_shared.cpp:3351-3372,
/// 7230-7263) through the condition driver: the puff's name by team for a
/// non-local spy, the local spy's sound-only disguise, the stealthed enemy's
/// missing puff, and the drop's sound.
/// </summary>
public sealed class TFDisguiseEffectsTests
{
    /// <summary>The disguise-wearing spy: entry slot 5.</summary>
    private static readonly EntityHandle Spy = new(0x0002_0005u);

    [Fact]
    public void Disguising_a_nonlocal_red_spy_puffs_red_and_speaks()
    {
        var rig = New();

        rig.Arm.OnAdded(Spy, Cond.Disguising);

        var start = Assert.Single(rig.Engine.Calls, c => c.Member == "Effects.ParticleSystemStart");
        Assert.Equal(Spy, start.ArgAs<EntityHandle>(0));
        Assert.Equal("spy_start_disguise_red", start.ArgAs<string>(1));

        var emit = Assert.Single(rig.Sounds.Emitted);
        Assert.Equal(Spy, emit.Entity);
        Assert.Equal("Player.Spy_Disguise", emit.Name);
    }

    [Fact]
    public void The_local_spy_hears_the_sound_with_no_particle()
    {
        var rig = New(localIndex: 5);

        rig.Arm.OnAdded(Spy, Cond.Disguising);

        Assert.Equal("Player.Spy_Disguise", Assert.Single(rig.Sounds.Emitted).Name);
        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member.StartsWith("Effects.ParticleSystem"));
    }

    [Fact]
    public void A_stealthed_enemy_spy_gets_the_sound_and_no_particle()
    {
        var rig = New(stealth: Cond.Stealthed);   // blue local, red spy: an enemy viewer

        rig.Arm.OnAdded(Spy, Cond.Disguising);

        Assert.Equal("Player.Spy_Disguise", Assert.Single(rig.Sounds.Emitted).Name);
        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member.StartsWith("Effects.ParticleSystem"));
    }

    [Fact]
    public void Dropping_the_disguise_plays_the_sound()
    {
        var rig = New();

        rig.Arm.OnRemoved(Spy, Cond.Disguised);

        var emit = Assert.Single(rig.Sounds.Emitted);
        Assert.Equal(Spy, emit.Entity);
        Assert.Equal("Player.Spy_Disguise", emit.Name);
    }

    [Fact]
    public void Only_the_disguise_bits_reach_the_arm()
    {
        var rig = New();

        rig.Arm.OnAdded(Spy, Cond.Burning);
        rig.Arm.OnRemoved(Spy, Cond.Burning);
        rig.Arm.OnRemoved(Spy, Cond.Disguising);

        Assert.Empty(rig.Sounds.Emitted);
        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member.StartsWith("Effects.ParticleSystem"));
    }

    /// <summary>
    /// The arm over an engine whose starts succeed. The spy stands in slot 5;
    /// the local viewer's slot, both teams and the spy's stealth bit are the
    /// fact's choices, as the server writes them onto the two resources.
    /// </summary>
    private static Rig New(int localIndex = 4, TFTeam localTeam = TFTeam.Blue, TFTeam spyTeam = TFTeam.Red,
                           Cond? stealth = null)
    {
        var engine = new ClientEffects();
        engine.Returns("Effects.ParticleSystemStart", true);
        engine.Returns("Hud.LocalPlayerIndex", localIndex);

        var resources = new PlayerResources();
        var resource = new client::TF2.Client.Player.ClientTFPlayerResource(engine, resources);
        resource.State.BindLocal();
        resources.Live = resource;
        resource.State.Team[localIndex] = (byte)localTeam;
        resource.State.Team[5] = (byte)spyTeam;

        var carriers = new ClientCarriers();
        if (stealth is { } bit)
        {
            var log = new client::SourceSharp.Abi.RecordingLog();
            var classes = new client::TF2.Shared.TFClassTable(log, engine);
            var carrier = new ClientTFPlayerData(engine, new ClientCarriers(), new NoShells(), resources,
                                                 engine, classes, new ClientDriver(new HookList([])), log);
            carrier.State.BindLocal();
            carrier.State.OwnerIndex = Spy.EntryIndex;
            ClientWords.SetConditions(carrier.State, Mask.Of(bit));
            carriers.Track(carrier);
        }

        var sounds = new SpySounds();
        return new Rig(engine, sounds, new Disguise(engine, engine, carriers, resources, sounds));
    }

    private sealed record Rig(ClientEffects Engine, SpySounds Sounds, Disguise Arm);

    /// <summary>The emits the arm asked for, with the entity each rode.</summary>
    private sealed class SpySounds : Sounds
    {
        public readonly List<(EntityHandle Entity, string Name)> Emitted = [];

        public void EmitFrom(EntityHandle entity, string soundName, Vector? origin = null)
            => Emitted.Add((entity, soundName));

        public void StopFrom(EntityHandle entity, string soundName) { }
    }

    /// <summary>A directory of nothing: the carrier's own lookups answer empty.</summary>
    private sealed class NoShells : client::SourceSharp.Client.IClientStandalones
    {
        public IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live() => [];

        public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }
}
