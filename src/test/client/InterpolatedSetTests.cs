using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The interpolation pass's list and delay (lane M1-A: the shell's
/// <c>WantsInterpolation</c> filter and <c>StandaloneInterpolationAmount</c>):
/// every block but the local viewmodel's and the replay-driven player's, the
/// admitted set handed back for the shell's PreRender walk, and stock's
/// <c>C_BaseEntity::GetInterpolationAmount</c> for the delay.
/// </summary>
public class InterpolatedSetTests
{
    //=========================================================================
    // The delay.
    //=========================================================================

    [Fact]
    public void TheDelayIsTheInterpRoundedToTicksPlusOne()
    {
        // 0.1 s at 15 ms ticks is 6.67 ticks, rounded to 7, plus one: 8 ticks.
        Assert.Equal(0.015f * 8, InterpolationDelay.Amount(0.1f, 0.015f, alternateTicks: false), 5);
    }

    [Fact]
    public void AlternateTicksAddATickMore()
        => Assert.Equal(0.015f * 9, InterpolationDelay.Amount(0.1f, 0.015f, alternateTicks: true), 5);

    [Fact]
    public void WithNoTickIntervalThereIsNoDelay()
        => Assert.Equal(0f, InterpolationDelay.Amount(0.1f, 0f, alternateTicks: false));

    [Fact]
    public void OnlyASinglePlayerGameSimulatesOnAlternateTicks()
    {
        int asked = 0;
        string? On() { asked++; return "1"; }

        Assert.True(InterpolationDelay.AlternateTicks(1, On));
        Assert.False(InterpolationDelay.AlternateTicks(8, On));
        Assert.Equal(1, asked);   // a multiplayer game does not read the convar
    }

    [Fact]
    public void AlternateTicksNeedTheConVarOn()
        => Assert.False(InterpolationDelay.AlternateTicks(1, () => "0"));

    //=========================================================================
    // The set.
    //=========================================================================

    private sealed class Blocks(params uint[] handles) : IInterpolationBlocks
    {
        public readonly byte[][] Raw = Make(handles);

        private static byte[][] Make(uint[] handles)
        {
            var raw = new byte[handles.Length][];
            for (int i = 0; i < handles.Length; i++)
            {
                raw[i] = new byte[EngineBlock.SizeBytes];
                BitConverter.TryWriteBytes(raw[i].AsSpan(HandleAt, 4), handles[i]);
            }
            return raw;
        }

        public int Begin(out float interpolationAmount)
        {
            interpolationAmount = 0.1f;
            return Raw.Length;
        }

        public Span<byte> Block(int index) => Raw[index];
    }

    private sealed class Admission : IInterpolationAdmission
    {
        public readonly List<uint[]> Sets = [];

        public void Admitted(ReadOnlySpan<uint> handles) => Sets.Add(handles.ToArray());
    }

    private static readonly int HandleAt = (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Head))
                                         + (int)Marshal.OffsetOf<EngineBlockHead>(nameof(EngineBlockHead.Handle));

    private static readonly int ViewModelAt = (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Client))
                                            + (int)Marshal.OffsetOf<EngineBlockClientTail>(nameof(EngineBlockClientTail.ViewModel));

    private static InterpolatedSet Set(Blocks blocks, Admission admission, uint driven = 0xFFFFFFFFu)
        => new(blocks, () => driven, () => 0.015f, () => false, admission);

    [Fact]
    public void EveryOrdinaryEntityIsAdmitted()
    {
        var admission = new Admission();

        int n = Set(new Blocks(11, 12, 13), admission).Begin(out _);

        Assert.Equal(3, n);
        Assert.Equal([11u, 12u, 13u], Assert.Single(admission.Sets));
    }

    [Fact]
    public void TheViewModelIsLeftOut()
    {
        var blocks = new Blocks(11, 12, 13);
        BitConverter.TryWriteBytes(blocks.Raw[1].AsSpan(ViewModelAt, 4), 1);
        var admission = new Admission();
        var set = Set(blocks, admission);

        Assert.Equal(2, set.Begin(out _));
        Assert.Equal([11u, 13u], admission.Sets[0]);
        Assert.Equal(13u, BitConverter.ToUInt32(set.Block(1).Slice(HandleAt, 4)));
    }

    [Fact]
    public void TheDrivenPlayerIsLeftOut()
    {
        var admission = new Admission();

        int n = Set(new Blocks(11, 12, 13), admission, driven: 12).Begin(out _);

        Assert.Equal(2, n);
        Assert.Equal([11u, 13u], admission.Sets[0]);
    }

    [Fact]
    public void TheSetAnswersTheDelayNotTheRawInterp()
    {
        Set(new Blocks(11), new Admission()).Begin(out float amount);

        Assert.Equal(0.015f * 8, amount, 5);
    }

    [Fact]
    public void AnEmptyListAdmitsNothingAndSaysSo()
    {
        var admission = new Admission();

        Assert.Equal(0, Set(new Blocks(), admission).Begin(out _));
        Assert.Empty(Assert.Single(admission.Sets));
    }

    //=========================================================================
    // The shell carries the set out and decides nothing.
    //=========================================================================

    private static string CodeOnly(string text)
    {
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\n]*", "");
    }

    [Fact]
    public void TheShellNoLongerFiltersTheList()
    {
        string shell = CodeOnly(File.ReadAllText(RepoLayout.Repo("src", "client", "native", "shell_standalone_client.cpp")));
        string header = CodeOnly(File.ReadAllText(RepoLayout.Repo("src", "client", "native", "shell_standalone_client.h")));

        Assert.Contains("SourceSharp_ClientInterpAdmitted(", shell);   // the control: this is the shell
        Assert.DoesNotContain("WantsInterpolation", shell + header);
        Assert.DoesNotContain("TIME_TO_TICKS", shell);
    }
}
