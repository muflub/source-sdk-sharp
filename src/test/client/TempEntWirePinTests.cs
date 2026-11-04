using System.Security.Cryptography;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Every declaration's bytes, pinned. The table is what the generated write produced once it was
/// shown equal to the reflective writer byte for byte; a row moves only when its class's wire
/// does, which is a change to review.
/// </summary>
public class TempEntWirePinTests
{
    private const int Seeds = 8;

    private static readonly Dictionary<string, string> Pinned = new(StringComparer.Ordinal)
    {
        ["CBaseTempEntity"] = "E3B0C44298FC1C14",
        ["CEffectData"] = "AAD6545C1E132DAF",
        ["CTEArmorRicochet"] = "7D8B87E7F6BD53E6",
        ["CTEBSPDecal"] = "39F0CBFDA7498773",
        ["CTEBaseBeam"] = "901DD5A72B74A9C0",
        ["CTEBeamEntPoint"] = "6D81240C49F4275D",
        ["CTEBeamEnts"] = "ADB8016045D4DB0C",
        ["CTEBeamFollow"] = "88373A049FF8AA37",
        ["CTEBeamLaser"] = "ADB8016045D4DB0C",
        ["CTEBeamPoints"] = "7EA99FB2B25785CA",
        ["CTEBeamRing"] = "ADB8016045D4DB0C",
        ["CTEBeamRingPoint"] = "BB125C80540D40E0",
        ["CTEBeamSpline"] = "A8EE0C658E3FC79F",
        ["CTEBloodSprite"] = "8FB973F04B8E8A2C",
        ["CTEBloodStream"] = "5E1DE1B71344B80C",
        ["CTEBreakModel"] = "227DC31FA0A497D8",
        ["CTEBubbleTrail"] = "E77043C2937BEDC4",
        ["CTEBubbles"] = "E77043C2937BEDC4",
        ["CTEClientProjectile"] = "E77A059D6E689C4A",
        ["CTEDecal"] = "E77A059D6E689C4A",
        ["CTEDust"] = "E49273BD058CA3BB",
        ["CTEDynamicLight"] = "7F43A82ED5C89602",
        ["CTEEffectDispatch"] = "7481B1F628180537",
        ["CTEEnergySplash"] = "BD284C9B581D3E0F",
        ["CTEExplosion"] = "535C53BA417E31C1",
        ["CTEFizz"] = "8DC2FACAAC4CEF6D",
        ["CTEFootprintDecal"] = "8DD026E163D8E339",
        ["CTEGaussExplosion"] = "F8C3223E969FE045",
        ["CTEGlowSprite"] = "43EE7A82F9AD36EF",
        ["CTEImpact"] = "4EE35D6513C70E8B",
        ["CTEKillPlayerAttachments"] = "85449192FDCC25B8",
        ["CTELargeFunnel"] = "DE2545C0B9894A80",
        ["CTEMetalSparks"] = "7D8B87E7F6BD53E6",
        ["CTEMuzzleFlash"] = "552051A1D9E7F463",
        ["CTEParticleSystem"] = "4ECEA2C7B83FEFF4",
        ["CTEPhysicsProp"] = "933298A8D786D3DD",
        ["CTEPlayerAnimEvent"] = "4B6FD219D2888910",
        ["CTEPlayerDecal"] = "39F0CBFDA7498773",
        ["CTEProjectedDecal"] = "552051A1D9E7F463",
        ["CTEShatterSurface"] = "A666053CB6FF9199",
        ["CTEShowLine"] = "29BBB73E2A7C7814",
        ["CTESmoke"] = "30E157591477777A",
        ["CTESparks"] = "714417D183533091",
        ["CTESprite"] = "30E157591477777A",
        ["CTESpriteSpray"] = "AFF998ACDB5BE5D5",
        ["CTETFBlood"] = "4EE35D6513C70E8B",
        ["CTEWorldDecal"] = "0A02C8276EFE03F1",
    };

    private static IReadOnlyList<TempEntType> GameTypes()
    {
        var engine = Fake();
        engine.Strict = false;

        int next = 5;
        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", typeof(TF2.Client.ClientTeTFBlood).Assembly, engine);

        return registry.All.Where(t => t.Class.Declaring.Assembly != typeof(TempEntWirePinTests).Assembly).ToList();
    }

    private static byte[] Written(TempEntType type, object declaration)
    {
        var blob = new byte[type.BlobBytes];
        var writer = new TempEntWriter(type, blob);

        if (declaration is TeEffectData data)
            data.WriteMembers(ref writer);
        else
            ((TempEntState)declaration).WriteMembers(ref writer);

        return blob;
    }

    private static string Digest(TempEntType type)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        for (int seed = 0; seed < Seeds; seed++)
            hash.AppendData(Written(type, TempEntFill.Filled(type.Class.Declaring, seed)));

        return Convert.ToHexString(hash.GetHashAndReset())[..16];
    }

    [Fact]
    public void Every_declaration_writes_the_bytes_it_has_always_written()
    {
        IReadOnlyList<TempEntType> types = GameTypes();

        string shown = string.Join("\n", types.OrderBy(t => t.Class.ClassName, StringComparer.Ordinal)
                                              .Select(t => $"        [\"{t.Class.ClassName}\"] = \"{Digest(t)}\","));

        Assert.True(types.Count == Pinned.Count, $"{types.Count} declarations, {Pinned.Count} pinned; the table is\n{shown}");

        foreach (TempEntType type in types)
            Assert.True(Pinned[type.Class.ClassName] == Digest(type),
                $"{type.Class.ClassName}'s wire moved; if that is meant, the table is\n{shown}");
    }

    /// <summary>
    /// What a read fills, written again, is the same bytes: a member the read left out, or read
    /// from another's position, is a different blob. The write is what the table pins.
    /// </summary>
    [Fact]
    public void Every_declaration_read_and_written_again_is_the_same_bytes()
    {
        int compared = 0;

        foreach (TempEntType type in GameTypes())
        {
            for (int seed = 0; seed < Seeds; seed++)
            {
                object sent = TempEntFill.Filled(type.Class.Declaring, seed);
                byte[] blob = Written(type, sent);
                var te = new TempEntReader(type, blob);
                object back;

                if (sent is TeEffectData)
                {
                    TeEffectData data = default;
                    data.ReadMembers(in te);
                    back = data;
                }
                else
                {
                    var state = (TempEntState)Activator.CreateInstance(type.Class.Declaring)!;
                    state.ReadMembers(in te);
                    back = state;
                }

                Assert.True(blob.AsSpan().SequenceEqual(Written(type, back)),
                    $"{type.Class.ClassName} seed {seed}: sent\n{TempEntFill.Dump(sent)}\nback\n{TempEntFill.Dump(back)}");

                compared += blob.Count(b => b != 0);
            }
        }

        Assert.True(compared > 1000, $"{compared} set bytes compared");
    }
}
