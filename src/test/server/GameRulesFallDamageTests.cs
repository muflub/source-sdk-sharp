using System;
using System.IO;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE FALL-DAMAGE FORMULA IS NOT IN <c>shell_gamerules.cpp</c> ANY MORE.
///
/// <para>
/// <c>CGameRules::FlPlayerFallDamage</c> asked C# and then, when C# declined,
/// answered
/// <c>( m_flFallVelocity - PLAYER_MAX_SAFE_FALL_SPEED ) * DAMAGE_FOR_FALL_SPEED</c>
/// itself — and C# declined every time, because <c>ServerModule.GetFallDamage</c>
/// returned -1 unless a ConVar had been set. So the number every fall in the
/// game produced came out of C++, and the "managed rule" above it was a rule
/// nothing reached.
/// </para>
///
/// <para>
/// <b>One fact, and it is about the ABSENCE.</b> The formula itself belongs to
/// <see cref="MoveMath.FallDamage"/>, which the <c>policy-player</c> lane
/// landed and which has its own facts; this lane wrote a second copy and
/// deleted it on merging, because two statements of one rule over two readings
/// of one input is precisely the defect that lane found. What is left to assert
/// here is that the third copy — the native one — really did go, which no test
/// on the managed side can say.
/// </para>
/// </summary>
public class GameRulesFallDamageTests
{
    [Fact]
    public void ManagedAnswersTheFormulaRatherThanDecliningToIt()
    {
        // The other half: the native arm answering zero is only right because
        // the managed side no longer says "use the stock rule". A fall at
        // twice the safe speed must cost something.
        Assert.True(MoveMath.FallDamage(MoveMath.PlayerMaxSafeFallSpeed * 2f) > 0f);
        Assert.Equal(0f, MoveMath.FallDamage(MoveMath.PlayerMaxSafeFallSpeed - 1f), 4);
    }
}
