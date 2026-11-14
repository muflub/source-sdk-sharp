extern alias client;

using TF2.Descent.Rules.Content;
using TF2.Descent.Server.Bestiary;

using Xunit;

using Catalog = client::TF2.Descent.Client.Content.DescentCatalog;

namespace TF2.Descent.Tests.Ui;

/// <summary>
/// The robot rows a health bar names (plan_descent_logic UI6): the client reads robots,
/// champion affixes and statuses from the rules' own files, row for row, and the fingerprint
/// the bestiary's messages carry agrees on both sides.
/// </summary>
public sealed class RobotCatalogTests
{
    private static readonly DescentContent Rules = DescentContent.Shipped();

    [Fact]
    public void Every_robot_template_reads_as_the_rules_hold_it_in_their_order()
    {
        Assert.Equal(Rules.Robots.Rows.Select(r => (r.Id, r.Name)), new Catalog().Robots.Select(r => (r.Id, r.Name)));
    }

    [Fact]
    public void Every_champion_affix_reads_as_the_rules_hold_it_in_their_order()
    {
        Assert.Equal(Rules.Champions.Rows.Select(r => (r.Id, r.Name)), new Catalog().Champions.Select(r => (r.Id, r.Name)));
    }

    [Fact]
    public void Every_status_reads_in_the_order_its_bit_names_it()
    {
        Assert.Equal(Rules.Status.Rows.Select(r => r.Id), new Catalog().Statuses);
    }

    [Fact]
    public void The_robot_fingerprint_is_the_same_on_both_sides()
    {
        Assert.Equal(RobotKeys.Fingerprint(Rules), new Catalog().RobotFingerprint);
    }

    [Fact]
    public void The_robot_fingerprint_is_not_the_item_fingerprint()
    {
        // The two name different tables: a fact that compared one with itself would pass either way.
        Assert.NotEqual(new Catalog().Fingerprint, new Catalog().RobotFingerprint);
    }
}
