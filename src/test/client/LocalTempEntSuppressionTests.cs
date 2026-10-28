using SourceSharp.Client.TempEnts;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="LocalTempEntSuppression"/>, from stock c_te.cpp's
/// <c>C_TempEntsSystem::SuppressTE</c>: <c>if ( !CanPredict() ) return true;</c>,
/// then suppress when the filter has no recipient.
/// </summary>
public class LocalTempEntSuppressionTests
{
    [Fact]
    public void AnEffectPredictionCannotPlayIsSuppressedEvenWithARecipient()
    {
        Assert.True(LocalTempEntSuppression.Suppress(canPredict: false, recipientCount: 1));
    }

    [Fact]
    public void AnEffectWithNoRecipientIsSuppressed()
    {
        Assert.True(LocalTempEntSuppression.Suppress(canPredict: true, recipientCount: 0));
    }

    [Fact]
    public void AnEffectThatCanPredictAndHasARecipientPlays()
    {
        Assert.False(LocalTempEntSuppression.Suppress(canPredict: true, recipientCount: 1));
    }
}
