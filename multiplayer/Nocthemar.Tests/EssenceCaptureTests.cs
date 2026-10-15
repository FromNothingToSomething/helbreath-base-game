using Server.Nocthemar.Core;
using Server.Utils;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T62: essence capture (Essence Vial or capture passive), bag counting and removal by item id.</summary>
public class EssenceCaptureTests {
    private static GameWorldPlayer Player() => TestSupport.NewPlayer(null, null, TestSupport.ShippedContent(), TestSupport.EssenceItems());

    private static void Give(GameWorldPlayer player, int itemId, int quantity) {
        for (var i = 0; i < quantity; i++) {
            Assert.True(player.InventoryManager.TryCreateItem(itemId, null, out _));
        }
    }

    [Theory]
    [InlineData(true, true, EssenceCapture.Passive)]
    [InlineData(true, false, EssenceCapture.Passive)]
    [InlineData(false, true, EssenceCapture.UseVial)]
    [InlineData(false, false, EssenceCapture.Refused)]
    public void The_passive_comes_first_then_a_vial_then_nothing(bool passive, bool vial, EssenceCapture expected) {
        Assert.Equal(expected, EssenceCaptureRules.Decide(passive, vial));
    }

    [Fact]
    public void Without_a_vial_or_a_passive_the_capture_is_refused() {
        var player = Player();

        Assert.False(player.CanCaptureEssences);
        Assert.Equal(EssenceCapture.Refused, player.PlanEssenceCapture());
    }

    [Fact]
    public void A_vial_in_the_bag_allows_the_capture() {
        var player = Player();
        Give(player, MimicIds.EssenceVialItemId, 1);

        Assert.Equal(EssenceCapture.UseVial, player.PlanEssenceCapture());
        Assert.Equal(1, player.InventoryManager.CountItem(MimicIds.EssenceVialItemId));
    }

    [Fact]
    public void The_capture_passive_works_without_a_vial_and_spends_none() {
        var player = Player();
        Give(player, MimicIds.EssenceVialItemId, 2);
        player.Stats.Add(new StatModifier(StatId.EssenceCapture, ModifierKind.Flat, 1, ModifierSources.Skill(1220)));

        Assert.True(player.CanCaptureEssences);
        Assert.Equal(EssenceCapture.Passive, player.PlanEssenceCapture());
        Assert.Equal(2, player.InventoryManager.CountItem(MimicIds.EssenceVialItemId));
    }

    [Fact]
    public void The_passive_works_with_an_empty_bag() {
        var player = Player();
        player.Stats.Add(new StatModifier(StatId.EssenceCapture, ModifierKind.Flat, 1, ModifierSources.Skill(1220)));

        Assert.Equal(EssenceCapture.Passive, player.PlanEssenceCapture());
    }

    [Fact]
    public void Assimilation_is_a_learnable_capture_passive_of_the_shapeshifter() {
        var player = Player();
        TestSupport.Restore(player, new NocState(NocState.CurrentV, 5, 0, 0, RaceId: 0, ClassId: 2));

        Assert.False(player.CanCaptureEssences);
        Assert.Null(player.TryLearnSkill(1220));

        Assert.True(player.CanCaptureEssences);
        Assert.Equal(EssenceCapture.Passive, player.PlanEssenceCapture());
    }

    [Fact]
    public void One_vial_is_consumed_per_capture() {
        var player = Player();
        Give(player, MimicIds.EssenceVialItemId, 3);

        Assert.True(player.InventoryManager.TryRemoveItemsById(MimicIds.EssenceVialItemId, 1, out var result));

        Assert.Equal(2, player.InventoryManager.CountItem(MimicIds.EssenceVialItemId));
        Assert.Single(result.AddedToBag);
        Assert.Equal(2, result.AddedToBag[0].Quantity);
        Assert.Empty(result.RemovedFromBagItemUids);
    }

    [Fact]
    public void The_last_vial_removes_its_row() {
        var player = Player();
        Give(player, MimicIds.EssenceVialItemId, 1);

        Assert.True(player.InventoryManager.TryRemoveItemsById(MimicIds.EssenceVialItemId, 1, out var result));

        Assert.Equal(0, player.InventoryManager.CountItem(MimicIds.EssenceVialItemId));
        Assert.Single(result.RemovedFromBagItemUids);
        Assert.Empty(result.AddedToBag);
        Assert.Equal(EssenceCapture.Refused, player.PlanEssenceCapture());
    }

    [Fact]
    public void Removal_is_all_or_nothing() {
        var player = Player();
        Give(player, 5000, 2);

        Assert.False(player.InventoryManager.TryRemoveItemsById(5000, 3, out var result));
        Assert.False(player.InventoryManager.TryRemoveItemsById(5000, 0, out _));
        Assert.False(player.InventoryManager.TryRemoveItemsById(5001, 1, out _));

        Assert.Equal(2, player.InventoryManager.CountItem(5000));
        Assert.Empty(result.AddedToBag);
        Assert.Empty(result.RemovedFromBagItemUids);
    }

    [Fact]
    public void Counting_ignores_other_items() {
        var player = Player();
        Give(player, 5000, 2);
        Give(player, 5001, 5);

        Assert.Equal(2, player.InventoryManager.CountItem(5000));
        Assert.Equal(5, player.InventoryManager.CountItem(5001));
        Assert.Equal(0, player.InventoryManager.CountItem(5002));
    }
}
