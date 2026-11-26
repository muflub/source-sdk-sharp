extern alias client;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Shared.Anim;

using C = client::SourceSharp.Abi;
using CC = client::SourceSharp.Client;
using CS = client::SourceSharp.Client.Standalone;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// One player's look, posed by the server's machine (which poses his hitboxes)
/// and by another client's (which draws him), lands on the same torso dials:
/// the server writes the pitch it posed with into the player's networked state,
/// the client poses from that word (<c>RemotePlayerLooks</c>), and both take
/// the body's yaw. Held for TF2's rig (body_pitch / body_yaw, the pitch
/// negated) and HL2DM's (aim_pitch / aim_yaw, the pitch as is), for a player
/// looking 40 degrees up with his eyes turned 40 degrees from his body.
/// </summary>
public class PlayerLookParityTests
{
    private const string Model = "models/player/rig.mdl";
    private const uint HandleRaw = 0x0001_0001u;
    private const float EyePitch = -40f;
    private const float EyeYaw = 40f;

    private static readonly Dictionary<string, int> TFRig = new()
    {
        ["move_x"] = 0, ["move_y"] = 1, ["body_pitch"] = 2, ["body_yaw"] = 3,
    };

    private static readonly Dictionary<string, int> HlRig = new()
    {
        ["aim_pitch"] = 2, ["aim_yaw"] = 3, ["move_yaw"] = 4, ["move_scale"] = 5,
    };

    private sealed class Descriptors : IModelDescriptorSource
    {
        public ModelDescriptor? Describe(int modelIndex) => ModelDescriptor.Create(
            1, [], [], [new ModelSequence(0, "idle", "ACT_IDLE", 0x1, 2f, [])]);
    }

    /// <summary>The server's player and what its machine wrote, the pitch it networks beside them.</summary>
    private static (Dictionary<int, float> Dials, float Networked) Server(Dictionary<string, int> rig)
    {
        var engine = Fake();
        engine.Returns("EntityRender.GetModelName", Model);
        engine.Returns("Entities.GetAngles", Vector.Zero);
        engine.Returns("Entities.GetEyeAngles", new Vector(EyePitch, EyeYaw, 0f));
        engine.Ignores("EntityRender.SequenceState", "EntityRender.SequenceState.sequence",
                       "EntityRender.SequenceState.cycle", "EntityRender.SequenceState.duration",
                       "EntityRender.SequenceState.looping", "EntityRender.SequenceState.starts",
                       "EntityRender.SetSequence", "EntityRender.SetPoseParameter",
                       "EntityRender.GetPoseParameter", "EntityRender.SequenceGroundSpeed");
        engine.ReturnsEachCall("EntityRender.LookupPoseParameter",
            () => rig.GetValueOrDefault(engine.AllOf("EntityRender.LookupPoseParameter").Last().ArgAs<string>(1), -1));

        var models = new ModelDescriptors(_ => 1, _ => Model, new RecordingLog());
        models.AttachSource(new Descriptors());
        var player = new SsPlayer(engine, engine, Impacts(engine), Fluid(), InfiniteAux(), PrintDamage(),
                                  models: models, clock: engine)
        {
            Handle = new EntityHandle(HandleRaw),
        };
        player.BindAreas(Areas(engine));
        player.State.BindLocal();
        player.State.Alive = true;
        player.State.MaxSpeed = 320f;
        player.State.Flags = 1u;

        for (int tick = 0; tick < 4; tick++)
            player.UpdateAnimState(10f + tick * 0.015f);

        return (LastWrites(engine.AllOf("EntityRender.SetPoseParameter").Select(c => (c.ArgAs<int>(1), c.ArgAs<float>(2)))),
                player.State.EyePitch);
    }

    private sealed class Interp : CC.IInterpolationAmount
    {
        public float Amount => 0.1f;
    }

    private sealed class OnePlayer(CS.ClientSsPlayer[] holder) : CC.IClientStandalones
    {
        public IReadOnlyList<CS.ClientStandaloneEntity> Live() => [holder[0]];
        public CS.ClientStandaloneEntity? GetByIndex(int entityIndex) => entityIndex == holder[0].Index ? holder[0] : null;
    }

    /// <summary>Another client's copy of the player, decoded with <paramref name="networked"/>, and what its machine wrote.</summary>
    private static Dictionary<int, float> Client(Dictionary<string, int> rig, float networked)
    {
        var engine = ClientDoubles.Fake();
        engine.Returns("EntityRender.LookupSequenceByActivity", 7);
        engine.Returns("EntityRender.SequenceGroundSpeed", 0f);
        engine.Returns("EntityRender.SetPoseParameter", 0f);
        engine.Returns("EntityRender.GetPoseParameter", 0f);
        engine.Returns("EntityRender.IsDormant", false);
        engine.Returns("EntityRender.GetModelName", Model);
        engine.Returns("Entities.GetAngles", new Vector(0f, 0f, 0f));
        engine.Returns("Hud.LocalPlayerIndex", 0);
        double t = 5.0;
        engine.ReturnsEachCall("Clock.Now", () => (float)(t += 0.05));
        engine.ReturnsEachCall("EntityRender.LookupPoseParameter",
            () => rig.GetValueOrDefault(engine.AllOf("EntityRender.LookupPoseParameter").Last().ArgAs<string>(1), -1));

        var holder = new CS.ClientSsPlayer[1];
        var looks = new CS.RemotePlayerLooks(new OnePlayer(holder), engine, new Interp(), engine);
        var player = new CS.ClientSsPlayer(engine, clock: engine, screen: engine, looks: looks)
        {
            Handle = new EntityHandle(HandleRaw),
        };
        holder[0] = player;
        player.BindEngine(engine, engine);
        player.BindIndex((int)(HandleRaw & 0x7FF));
        player.State.BindLocal();
        player.State.Alive = true;
        player.State.MaxSpeed = 320f;
        player.State.Flags = 1u;
        player.State.EyePitch = networked;

        player.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        player.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
        for (int frame = 0; frame < 4; frame++)
            looks.ClientThink(0.015f);

        return LastWrites(engine.AllOf("EntityRender.SetPoseParameter").Select(c => (c.ArgAs<int>(1), c.ArgAs<float>(2))));
    }

    private static Dictionary<int, float> LastWrites(IEnumerable<(int Index, float Value)> writes)
    {
        var last = new Dictionary<int, float>();
        foreach ((int index, float value) in writes)
            last[index] = value;
        return last;
    }

    private static void Agree(Dictionary<string, int> rig, string pitchDial, string yawDial, float expectedPitch)
    {
        var (server, networked) = Server(rig);
        var client = Client(rig, networked);
        int pitch = rig[pitchDial], yaw = rig[yawDial];

        Assert.True(server.TryGetValue(pitch, out float sp) && client.TryGetValue(pitch, out float cp) && sp == cp && sp == expectedPitch,
            $"{pitchDial}: server {server.GetValueOrDefault(pitch, float.NaN)}, client {client.GetValueOrDefault(pitch, float.NaN)}, stock's {expectedPitch}");
        Assert.True(server.TryGetValue(yaw, out float sy) && client.TryGetValue(yaw, out float cy) && sy == cy,
            $"{yawDial}: server {server.GetValueOrDefault(yaw, float.NaN)}, client {client.GetValueOrDefault(yaw, float.NaN)}");
    }

    [Fact]
    public void TheServersAndAClientsTorsoDialsAgreeOnTF2sRig() => Agree(TFRig, "body_pitch", "body_yaw", -EyePitch);

    [Fact]
    public void TheServersAndAClientsTorsoDialsAgreeOnHL2DMsRig() => Agree(HlRig, "aim_pitch", "aim_yaw", EyePitch);
}
