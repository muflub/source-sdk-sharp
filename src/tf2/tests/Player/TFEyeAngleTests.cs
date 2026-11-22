extern alias client;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Shared.Player;

using Xunit;

using History = client::TF2.Client.Player.TFEyeAngleHistory;
using static SourceSharp.Tests.TestServices;

namespace TF2.Tests.Player;

/// <summary>
/// <c>m_angEyeAngles</c>: CTFPlayer::PostThink copies EyeAngles() into it
/// (tf_player.cpp:2687), the send table carries the pitch in 8 bits over
/// [-90, 90] and the yaw as a 10-bit angle (:731-732), and C_TFPlayer
/// interpolates it (c_tf_player.cpp:3874) for EyeAngles(), which the
/// observer's in-eye view looks along. Before it, in-eye took the watched
/// body's facing: yaw only, never up or down.
/// </summary>
public sealed class TFEyeAngleTests
{
    // ---- the wire: stock's bits and stock's arithmetic ------------------------

    [Fact]
    public void The_pitch_is_eight_bits_over_minus_ninety_to_ninety()
    {
        Assert.Equal(0, TFEyeAngleWire.EncodePitch(-90f));
        Assert.Equal(255, TFEyeAngleWire.EncodePitch(90f));
        Assert.Equal(128, TFEyeAngleWire.EncodePitch(0.5f));   // (90.5) * 255/180 = 128.2
        Assert.Equal(-90f + 180f * 128 / 255, TFEyeAngleWire.DecodePitch(128), 4);
    }

    [Fact]
    public void A_pitch_out_of_range_clamps_as_stocks_float_does()
    {
        Assert.Equal(0, TFEyeAngleWire.EncodePitch(-120f));
        Assert.Equal(255, TFEyeAngleWire.EncodePitch(100f));
    }

    [Fact]
    public void The_yaw_is_a_ten_bit_angle_after_anglemod()
    {
        Assert.Equal(0, TFEyeAngleWire.EncodeYaw(0f));
        Assert.Equal(TFEyeAngleWire.EncodeYaw(10f), TFEyeAngleWire.EncodeYaw(370f));
        Assert.Equal(TFEyeAngleWire.EncodeYaw(270f), TFEyeAngleWire.EncodeYaw(-90f));
        Assert.Equal(270f, TFEyeAngleWire.DecodeYaw(TFEyeAngleWire.EncodeYaw(-90f)), 0);
        Assert.Equal(1023, TFEyeAngleWire.EncodeYaw(359.9f));
    }

    [Fact]
    public void The_yaws_range_multiplier_is_the_one_stock_squeezes()
    {
        // AssignRangeMultiplier: 1023/360 overshoots 1023 at 360 in float, so
        // 0.9999 of it is used: 180 encodes as 511 (511.45), not the 512 the
        // plain ratio's 511.5 rounds to.
        Assert.Equal(511, TFEyeAngleWire.EncodeYaw(180f));
    }

    [Theory]
    [InlineData(-45f, 30f)]
    [InlineData(12.5f, 200f)]
    [InlineData(89f, 359f)]
    public void A_round_trip_is_within_half_a_step(float pitch, float yaw)
    {
        Assert.InRange(TFEyeAngleWire.DecodePitch(TFEyeAngleWire.EncodePitch(pitch)) - pitch, -180f / 255 / 2, 180f / 255 / 2);
        Assert.InRange(TFEyeAngleWire.DecodeYaw(TFEyeAngleWire.EncodeYaw(yaw)) - yaw, -360f / 1023 / 2 - 0.01f, 360f / 1023 / 2 + 0.01f);
    }

    [Fact]
    public void The_fields_carry_stocks_bit_counts()
    {
        var fields = StandaloneNetStates.Of(typeof(TFPlayerNetState)).Fields;
        Assert.Equal(8, fields.Single(f => f.Name == nameof(TFPlayerNetState.EyePitch)).Bits);
        Assert.Equal(10, fields.Single(f => f.Name == nameof(TFPlayerNetState.EyeYaw)).Bits);
    }

    [Fact]
    public void A_pitch_carried_as_zero_to_three_sixty_reads_signed()
    {
        Assert.Equal(-10f, TFEyeAngleWire.SignedPitch(350f), 3);
        Assert.Equal(30f, TFEyeAngleWire.SignedPitch(30f), 3);
    }

    // ---- the server's copy -------------------------------------------------

    private sealed class OneEntity(EntityHandle handle, ScriptedEntity entity) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h) => h == handle ? entity : null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => true;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    [Fact]
    public void Each_frame_copies_every_players_eye_angles_into_its_carrier()
    {
        const int index = 4;
        var player = new EntityHandle(0x0001_0000u | index);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsPlayer).Assembly);
        var services = Services(out var engine);
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.GetIndex", index)
              .Returns("Entities.GetEyeAngles", new Vector(350f, 90f, 0f))
              .Returns("Entities.Create", new EntityHandle(0x0002_0050u))
              .Returns("Entities.GetOrigin", default(Vector));
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        registry.KeyValue(player, EntityNames.Player, "targetname", "p");
        ((SsPlayer)registry.Get(player)!).State.BindLocal();

        var carriers = new TFPlayerCarriers();
        var entity = new TFPlayerDataEntity(engine);
        entity.State.BindLocal();
        var carrier = new TFPlayerCarrier(engine, new OneEntity(new EntityHandle(0x0002_0050u), entity), carriers, new RecordingLog());
        carrier.Attach(player);

        new TFEyeAngles(registry, engine, carriers).Copy();

        Assert.Equal(TFEyeAngleWire.EncodePitch(-10f), entity.State.EyePitch);
        Assert.Equal(TFEyeAngleWire.EncodeYaw(90f), entity.State.EyeYaw);
    }

    // ---- the client's history ----------------------------------------------

    private const float Amount = 0.1f;

    [Fact]
    public void The_read_is_the_interpolation_amount_in_the_past_between_two_arrivals()
    {
        var history = new History();
        history.Note(3, 1, TFEyeAngleWire.EncodePitch(0f), TFEyeAngleWire.EncodeYaw(0f), 10.0f, Amount);
        history.Note(3, 2, TFEyeAngleWire.EncodePitch(0f), TFEyeAngleWire.EncodeYaw(90f), 10.1f, Amount);

        Assert.True(history.TryRead(3, 10.15f, Amount, out QAngle half));

        Assert.InRange(half.Yaw, 44f, 46f);   // 10.05 is half way from 10.0 to 10.1
    }

    [Fact]
    public void The_pitch_is_read_as_well_as_the_yaw()
    {
        var history = new History();
        history.Note(3, 1, TFEyeAngleWire.EncodePitch(-40f), TFEyeAngleWire.EncodeYaw(30f), 10.0f, Amount);

        Assert.True(history.TryRead(3, 11f, Amount, out QAngle eyes));

        Assert.InRange(eyes.Pitch, -40.5f, -39.5f);
        Assert.InRange(eyes.Yaw, 29.5f, 30.5f);
    }

    [Fact]
    public void A_frame_with_no_new_decode_files_nothing()
    {
        var history = new History();
        history.Note(3, 1, TFEyeAngleWire.EncodePitch(0f), TFEyeAngleWire.EncodeYaw(0f), 10.0f, Amount);
        history.Note(3, 1, TFEyeAngleWire.EncodePitch(0f), TFEyeAngleWire.EncodeYaw(90f), 10.1f, Amount);

        Assert.True(history.TryRead(3, 10.15f, Amount, out QAngle eyes));

        Assert.InRange(eyes.Yaw, -0.5f, 0.5f);
    }

    [Fact]
    public void A_player_never_filed_has_no_answer()
        => Assert.False(new History().TryRead(5, 10f, Amount, out _));
}
