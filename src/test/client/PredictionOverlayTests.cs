using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The engine block in front of a predicted state is the server's to write and
/// nothing on the client predicts it. The weapon and viewmodel compares judged
/// it anyway, so every time the server moved the carried pair to its owner the
/// origin's low bytes stood as "(padding@24), (padding@25), (padding@26)"
/// mispredictions. These facts pin the rule both compares now share.
/// </summary>
public class PredictionOverlayTests
{
    // Head.Origin is at byte 16 of the engine block; z is 24..27.
    private const int OriginZ = 24;

    private static NetStateCatalog Catalog() => NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog());

    private static (byte[] Predicted, int DeclaredStart) Image(Type stateType)
    {
        int size = StandaloneNetStates.Of(stateType)!.Size;
        int start = PredictionJudge.DeclaredStart(stateType, size);
        return (new byte[size], start);
    }

    [Theory]
    [InlineData(typeof(WeaponNetState), EntityNames.SsWeapon)]
    [InlineData(typeof(ViewModelNetState), EntityNames.SsViewModel)]
    public void TheServerMovingTheCarriedPairIsNotAMisprediction(Type stateType, string className)
    {
        var (predicted, start) = Image(stateType);

        Assert.True(start > OriginZ + 3, "the declared fields start after the engine block's origin");

        byte[] arrived = (byte[])predicted.Clone();
        arrived[OriginZ] = 0x7F;
        arrived[OriginZ + 1] = 0x7F;
        arrived[OriginZ + 2] = 0x3F;

        uint typeId = NetSchema.TypeIdOf(className);

        int raw = PredictionJudge.Judge(Catalog(), typeId, predicted, arrived, out _, out _);
        Assert.True(raw > 0, "judged raw, the server's origin write stands (the bug's shape)");

        byte[] judged = PredictionJudge.OverlayDeclared(predicted, arrived, start);
        Assert.True(judged.AsSpan().SequenceEqual(arrived),
            "with the engine block taken from the arrival there is nothing to judge");
    }

    [Theory]
    [InlineData(typeof(WeaponNetState), EntityNames.SsWeapon)]
    [InlineData(typeof(ViewModelNetState), EntityNames.SsViewModel)]
    public void ADeclaredFieldThatDiffersStillStands(Type stateType, string className)
    {
        var (predicted, start) = Image(stateType);

        byte[] arrived = (byte[])predicted.Clone();
        arrived[start] = 0x05;

        byte[] judged = PredictionJudge.OverlayDeclared(predicted, arrived, start);

        int standing = PredictionJudge.Judge(Catalog(), NetSchema.TypeIdOf(className), judged, arrived,
                                             out string fields, out _);
        Assert.True(standing > 0, $"a declared field's difference is still a misprediction ({fields})");
    }
}
