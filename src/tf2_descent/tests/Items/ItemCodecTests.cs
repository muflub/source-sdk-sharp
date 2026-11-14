using System.Text;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Items;

/// <summary>
/// The item's bytes (plan_descent_logic RU3): schema 2 is canonical and hand-written, carries
/// the rolled half, the mutable half and the history; schema 1 (the first rules' bytes) still
/// decodes and replays.
/// </summary>
public sealed class ItemCodecTests
{
    private static readonly TF2DescentRules R = NewRules();

    /// <summary>A schema 1 item exactly as the first rules' reflection serializer wrote it.</summary>
    private const string V1 =
        "{\"Seed\":99,\"BaseType\":\"minigun\",\"Rarity\":1,\"ItemLevel\":8,\"Count\":1,\"Identified\":true,\"Tier\":1," +
        "\"Context\":{\"CharacterClass\":\"medic\",\"CharacterLevel\":5,\"Depth\":2,\"ItemLevel\":8,\"Tier\":1,\"LootTable\":\"kill\",\"Source\":\"standard\"}}";

    [Fact]
    public void The_codec_is_schema_2()
    {
        Assert.Equal(2, R.Items.SchemaVersion);
        Assert.Equal(2, R.RollItem(Ctx(2), 1).SchemaVersion);
    }

    [Fact]
    public void Decoding_and_encoding_an_item_gives_the_same_bytes()
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            var item = R.RollItem(Ctx((int)(seed % 5)), seed);
            var decoded = R.Items.Decode(item.Instance, item.SchemaVersion);

            Assert.Equal(item.Instance, ItemCodec.Encode(R.ItemRules.Codec.Instance(decoded)));
            Assert.Equal((item.BaseType, item.Rarity, item.ItemLevel, item.Identified, item.Tier, item.Context),
                (decoded.BaseType, decoded.Rarity, decoded.ItemLevel, decoded.Identified, decoded.Tier, decoded.Context));
        }
    }

    [Fact]
    public void The_mutable_half_and_the_history_survive_the_bytes()
    {
        var roller = R.ItemRules.Roller;
        var item = roller.Reroll(roller.Roll(Ctx(2), 3), 0, 12345678901234567890UL) with { Durability = 7, WearUses = 3, Identified = true };

        var back = ItemCodec.DecodeInstance(ItemCodec.Encode(item));

        Assert.Equal((7, 3, 1, true), (back.Durability, back.WearUses, back.RerollCount, back.Identified));
        Assert.Equal(item.History.ToArray(), back.History.ToArray());
        Assert.Equal(12345678901234567890UL, back.History[0].Seed);
    }

    [Fact]
    public void Json_round_trips_through_the_host_codec_members()
    {
        var item = R.RollItem(Ctx(3), 44);

        var back = R.Items.FromJson(R.Items.ToJson(item));

        Assert.Equal(item.Instance, back.Instance);
        Assert.True(R.Replays(back));
    }

    [Fact]
    public void A_field_nobody_reads_is_refused()
    {
        var json = Encoding.UTF8.GetString(R.RollItem(Ctx(1), 2).Instance).Replace("\"count\":1", "\"count\":1,\"bonus\":9");

        Assert.Throws<InvalidDataException>(() => R.Items.Decode(Encoding.UTF8.GetBytes(json), 2));
    }

    [Fact]
    public void A_schema_1_item_still_decodes()
    {
        var item = R.Items.Decode(Encoding.UTF8.GetBytes(V1), 1);

        Assert.Equal((99UL, "minigun", 1, 8, 1, 1), (item.Seed, item.BaseType, item.Rarity, item.ItemLevel, item.Count, item.SchemaVersion));
        Assert.Equal(new ItemRollContext("medic", 5, 2, 8, 1, "kill", "standard"), item.Context);
        Assert.Equal(V1, R.Items.ToJson(item));
    }

    [Fact]
    public void A_schema_1_item_replays_by_the_formula_that_rolled_it()
    {
        var ctx = new ItemRollContext("medic", 5, 2, 8, 1, "kill", "standard");
        var rolled = LegacyItems.Roll(ctx, 99);
        var decoded = R.Items.Decode(rolled.Instance, 1);

        Assert.True(R.Replays(decoded));
        Assert.False(R.Replays(decoded with { BaseType = rolled.BaseType == "knife" ? "wrench" : "knife" }));
    }

    [Fact]
    public void A_schema_1_json_is_told_from_a_schema_2_json()
    {
        Assert.Equal(1, R.Items.FromJson(V1).SchemaVersion);
        Assert.Equal(2, R.Items.FromJson(R.Items.ToJson(R.RollItem(Ctx(0), 1))).SchemaVersion);
    }

    [Fact]
    public void An_item_that_does_not_decode_never_replays()
    {
        var item = R.RollItem(Ctx(2), 9);

        Assert.False(R.Replays(item with { Instance = Encoding.UTF8.GetBytes("{\"seed\":\"x\"}") }));
        Assert.False(R.Replays(item with { Instance = [1, 2, 3] }));
    }
}
