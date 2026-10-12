using System.Reflection;
using System.Security.Cryptography;
using TF2.Descent.Rules;
using TF2.Descent.Server;
using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// TF2.Descent.Rules is replayed by the hosting service in a process of its own, so
/// it must give the same answer for the same inputs, every time, in any
/// instance (plan_host_sdk §2.1, P4).
/// </summary>
public sealed class RulesTests
{
    [Fact]
    public void Kill_rolls_and_item_rolls_are_identical_across_instances_for_a_thousand_seeds()
    {
        var a = new TF2DescentRules();
        var b = new TF2DescentRules();

        for (uint kill = 0; kill < 1000; kill++)
        {
            Assert.Equal(a.KillRoll(42, kill, "heavy", 3, 2), b.KillRoll(42, kill, "heavy", 3, 2));

            var ctx = new ItemRollContext("soldier", 10, 3, 12, (int)(kill % 4), "kill", "standard");
            Assert.Equal(a.RollItem(ctx, kill).Instance, b.RollItem(ctx, kill).Instance);
        }
    }

    [Fact]
    public void About_two_kills_in_three_drop_with_tiers_from_stock_to_unusual()
    {
        var rules = new TF2DescentRules();
        var rolls = Enumerable.Range(0, 3000).Select(i => rules.KillRoll(7, (uint)i, "scout", 1, 1)).ToArray();

        int drops = rolls.Count(r => r.Drop);
        Assert.InRange(drops, 1800, 2200);
        Assert.All(rolls.Where(r => r.Drop), r => Assert.InRange(r.Tier, 0, 3));
        Assert.All(rolls.Where(r => !r.Drop), r => Assert.Equal(-1, r.Tier));
    }

    [Fact]
    public void A_rolled_item_replays_and_a_tampered_one_does_not()
    {
        var rules = new TF2DescentRules();
        var item = rules.RollItem(new ItemRollContext("medic", 5, 2, 8, 1, "kill", "standard"), 99);

        Assert.True(rules.Replays(item));
        Assert.False(rules.Replays(item with { Rarity = item.Rarity + 1 }));
    }

    [Fact]
    public void A_checkpoint_that_changes_class_is_refused()
    {
        var rules = new TF2DescentRules();
        var before = rules.NewCharacter("soldier", "a", hardcore: false);
        var after = rules.NewCharacter("medic", "a", hardcore: false);

        Assert.False(rules.ValidateCheckpoint(before, after, new CheckpointEvidence([], 1)).Ok);
        Assert.True(rules.ValidateCheckpoint(before, before, new CheckpointEvidence([], 1)).Ok);
    }

    [Fact]
    public void The_assembly_is_marked_as_the_rules_module()
    {
        var mark = typeof(TF2DescentRules).Assembly.GetCustomAttribute<HostRulesModuleAttribute>();

        Assert.NotNull(mark);
        Assert.Contains("SourceSharp.Host.Contracts",
            typeof(TF2DescentRules).Assembly.GetReferencedAssemblies().Select(r => r.Name));
    }

    //-------------------------------------------------------------------------
    // PathModuleSource
    //-------------------------------------------------------------------------

    private sealed class Mod(string managedDir) : IModInfo
    {
        public string Id => "tf2_descent";
        public string GameDir => "/nowhere";
        public string ManagedDir => managedDir;
        public IReadOnlyList<string> AssembliesFor(Side side) => [];
    }

    [Fact]
    public void A_byte_loaded_module_is_described_by_its_path_in_the_managed_dir()
    {
        var dir = Directory.CreateTempSubdirectory("tf2_descent-managed-").FullName;
        try
        {
            var built = Path.Combine(AppContext.BaseDirectory, "TF2.Descent.Rules.dll");
            File.Copy(built, Path.Combine(dir, "TF2.Descent.Rules.dll"));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "SourceSharp.Host.Contracts.dll"),
                      Path.Combine(dir, "SourceSharp.Host.Contracts.dll"));

            var file = new PathModuleSource(new Mod(dir)).Find();

            Assert.NotNull(file);
            Assert.Equal("TF2.Descent.Rules", file!.Assembly);
            Assert.Equal(Path.Combine(dir, "TF2.Descent.Rules.dll"), file.Path);
            Assert.Equal(HostContract.Version, file.ContractVersion);
            Assert.DoesNotContain(file.Deps, d => d.Assembly == "SourceSharp.Host.Contracts");
            Assert.Equal(SHA256.HashData(File.ReadAllBytes(built)), SHA256.HashData(File.ReadAllBytes(file.Path)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void No_file_in_the_managed_dir_is_no_module()
    {
        var dir = Directory.CreateTempSubdirectory("tf2_descent-empty-").FullName;
        try
        {
            Assert.Null(new PathModuleSource(new Mod(dir)).Find());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
