using System;
using System.Collections.Generic;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="IEngineEntityNames"/>, the name lookup that left
/// <see cref="IEngineEntities"/> in plan_shim_areas_split phase 4, reached
/// through the binding a side registers for it (EngineServices.Bindings), so
/// each fact also covers that it is registered.
/// </summary>
public class EngineEntityNamesTests
{
    private static readonly EntityHandle Lamp = new(0x0001_0007u);

    /// <summary>A side's shell-less answers that know one name.</summary>
    private sealed class OneName : IShellLessSide
    {
        public string? Asked;

        public NameMatches Named(string query, int limit)
        {
            Asked = query;
            return query == "lamp" ? NameMatches.Copy([Lamp]) : NameMatches.None;
        }

        public bool ClassIsShellLess(string className) => throw new NotSupportedException();
        public EntityHandle Create(string className, ReadOnlySpan<byte> packedKeys) => throw new NotSupportedException();
        public ShellLessEntity? StateOf(EntityHandle handle) => throw new NotSupportedException();
        public void Spawn(EntityHandle handle) => throw new NotSupportedException();
        public bool KeyValue(EntityHandle handle, string key, string value) => throw new NotSupportedException();
        public int FindByClassName(string query, List<EntityHandle> into) => throw new NotSupportedException();
        public int FindByName(string query, List<EntityHandle> into) => throw new NotSupportedException();
        public EntityHandle FindFirstNamed(string query) => throw new NotSupportedException();
        public int FindAllNamed(string query, List<EntityHandle> into, int limit) => throw new NotSupportedException();
        public int LiveCount => throw new NotSupportedException();
        public (long Allocated, long Released) AllocationTotals => throw new NotSupportedException();
    }

    private static IEngineEntityNames Binding(EngineSide side)
    {
        var composition = new ServiceContainer().AddInstance(new StandaloneMirror());
        foreach (var (contract, binding) in EngineServices.Bindings(side, composition))
        {
            if (contract == typeof(IEngineEntityNames))
                return (IEngineEntityNames)binding;
        }

        Assert.Fail("no binding is registered for IEngineEntityNames");
        return null!;
    }

    private static List<EntityHandle> Walk(NameMatches matches)
    {
        var seen = new List<EntityHandle>();
        foreach (EntityHandle h in matches)
            seen.Add(h);
        return seen;
    }

    [Fact]
    public void ANameIsAskedOfTheSidesShellLessAnswers()
    {
        var side = EngineSide.Create(Side.Server);
        var answers = new OneName();
        side.ShellLess.Bind(answers);

        Assert.Equal([Lamp], Walk(Binding(side).Named("lamp")));
        Assert.Equal("lamp", answers.Asked);
    }

    [Fact]
    public void AnEmptyNameMatchesNothingAndAsksNobody()
    {
        var side = EngineSide.Create(Side.Server);
        var answers = new OneName();
        side.ShellLess.Bind(answers);

        Assert.Empty(Walk(Binding(side).Named("")));
        Assert.Null(answers.Asked);
    }

    [Fact]
    public void ASideWithNoShellLessAnswersMatchesNothing()
    {
        var side = EngineSide.Create(Side.Server);

        Assert.Empty(Walk(Binding(side).Named("lamp")));
    }
}
