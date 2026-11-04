using SourceSharp.Shared;

using TF2.Shared.Econ;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Client;

/// <summary>
/// The flamethrower's pilot light: the attached model an item's
/// <c>visuals.attached_models</c> names, which stock draws riding the weapon's
/// bones (<c>CEconEntity::UpdateAttachmentModels</c>,
/// econ_entity.cpp:1079-1130) — the blue flame at the igniter tip.
/// </summary>
public sealed class TFPilotLightTests
{
    [Fact]
    public void The_flamethrowers_visuals_name_the_pilot_light()
    {
        var item = EconTestFiles.Trimmed().Items[21];

        // items_game.trimmed.txt:1556-1562, stock's attachedmodel_t with no
        // model_display_flags: MaskAll (econ_item_schema.cpp:2503), drawn in
        // both the world and the viewmodel pass.
        var attached = Assert.Single(item.AttachedModels);
        Assert.Equal("models/weapons/c_models/c_flamethrower/c_flamethrower_pilotlight.mdl", attached.Model);
        Assert.Equal(AttachedModelDisplayFlags.MaskAll, attached.DisplayFlags);
    }

    [Fact]
    public void An_item_without_attached_models_names_none()
    {
        var item = EconTestFiles.Trimmed().Items[13];

        Assert.Empty(item.AttachedModels);
    }

    [Fact]
    public void The_flamethrowers_row_carries_its_pilot_light()
    {
        var row = WeaponFixtures.Types(EconTestFiles.Fixture("items_game.trimmed.txt"))
            .Rows().ToDictionary(r => r.Name)["tf_weapon_flamethrower"];

        var attached = Assert.Single(row.AttachedModels);
        Assert.Equal("models/weapons/c_models/c_flamethrower/c_flamethrower_pilotlight.mdl", attached.Model);
        Assert.Equal(AttachedModelDisplayFlags.MaskAll, attached.DisplayFlags);
    }

    [Fact]
    public void A_row_whose_item_names_none_carries_none()
    {
        var row = WeaponFixtures.Types(EconTestFiles.Fixture("items_game.trimmed.txt"))
            .Rows().ToDictionary(r => r.Name)["tf_weapon_scattergun"];

        Assert.Empty(row.AttachedModels);
    }
}
