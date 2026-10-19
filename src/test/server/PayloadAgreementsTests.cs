using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What a <see cref="PayloadAgreement"/> says beyond its fields
/// (<see cref="PayloadAgreements"/>), which the dev API's net schema
/// route prints.
/// </summary>
public class PayloadAgreementsTests
{
    [Theory]
    [InlineData(PayloadAgreementState.Agrees, "agrees")]
    [InlineData(PayloadAgreementState.Disagrees, "disagrees")]
    [InlineData(PayloadAgreementState.NoPayload, "none")]
    [InlineData(PayloadAgreementState.NotSupplied, "unsupported")]
    public void EachStateHasTheNameTheJsonSpells(PayloadAgreementState state, string name)
        => Assert.Equal(name, new PayloadAgreement(state, 8, -1).Name());

    [Theory]
    [InlineData(PayloadAgreementState.Agrees, true)]
    [InlineData(PayloadAgreementState.Disagrees, false)]
    [InlineData(PayloadAgreementState.NoPayload, false)]
    [InlineData(PayloadAgreementState.NotSupplied, false)]
    public void OnlyAnAgreementAgrees(PayloadAgreementState state, bool agrees)
        => Assert.Equal(agrees, new PayloadAgreement(state, 8, -1).Agrees());

    [Fact]
    public void NotSuppliedHasNoWordsAndNoBadWord()
        => Assert.Equal(new PayloadAgreement(PayloadAgreementState.NotSupplied, 0, -1), PayloadAgreements.NotSupplied);
}
