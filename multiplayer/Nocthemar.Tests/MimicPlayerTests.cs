using Server.Nocthemar.Core;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T60: mimicry on the player: forge actions, slots (level + stat), cast experience, persistence round trip, load-time sanitizing and the no-content mode.</summary>
public class MimicPlayerTests {
    // Shipped abilities used below: 2000 Nuée (class 3), 2001 Morsure infectée, 2005 Frénésie, 2008 Putréfaction (open); fusion 2005 + 2008 -> 2102.
    private static GameWorldPlayer Player() => TestSupport.NewPlayer(null, null, TestSupport.ShippedContent());

    private static NocState Saved(int level = 1, int? classId = null, MimicEntry[]? mimics = null, int[]? equipped = null) =>
        new(NocState.CurrentV, level, 0, 0, RaceId: 0, ClassId: classId, Mimics: mimics, MimicEquipped: equipped);

    private static GameWorldPlayer Loaded(NocState noc) {
        var player = Player();
        TestSupport.Restore(player, noc);
        return player;
    }

    [Fact]
    public void Without_content_nothing_is_available_and_nothing_is_lost() {
        var player = TestSupport.NewPlayer();

        Assert.Equal("Mimicry is not available.", player.TryConsumeEssence(2001));
        Assert.Equal("Mimicry is not available.", player.TryFuseEssences(2005, 2008));
        Assert.Equal("Mimicry is not available.", player.TryEquipMimic(2001));
        Assert.Equal("Mimicry is not available.", player.TryUnequipMimic(2001));
        Assert.Equal(0, player.MimicSlotLimit);
        Assert.Equal(0, player.MimicLevel(2001));
        player.RegisterMimicCast(2001);
        TestSupport.Restore(player, Saved(mimics: [new MimicEntry(2001, 3)], equipped: [2001]));

        var state = player.CreatePersistenceState("aresden").Noc!;
        Assert.Equal(new MimicEntry(2001, 3), state.Mimics![0]);
        Assert.True(new[] { 2001 }.SequenceEqual(state.MimicEquipped!));
    }

    [Fact]
    public void A_new_player_has_one_slot_and_nothing_learned() {
        var player = Player();

        Assert.Equal(1, player.MimicSlotLimit);
        Assert.Empty(player.LearnedMimics);
        Assert.Empty(player.EquippedMimicIds);
    }

    [Fact]
    public void Consuming_an_essence_learns_it_once() {
        var player = Player();

        Assert.Null(player.TryConsumeEssence(2001));

        Assert.Equal(1, player.MimicLevel(2001));
        Assert.Equal(2001, player.LearnedMimics.Single().Id);
        Assert.Contains("already learned", player.TryConsumeEssence(2001));
        Assert.Contains("reserved to another class", player.TryConsumeEssence(2000));
        Assert.Contains("Unknown ability", player.TryConsumeEssence(5));
        Assert.Single(player.LearnedMimics);
    }

    [Fact]
    public void Fusing_two_essences_learns_the_result() {
        var player = Player();

        Assert.Null(player.TryFuseEssences(2008, 2005));

        Assert.Equal(2102, player.LearnedMimics.Single().Id);
        Assert.Contains("do not fuse", player.TryFuseEssences(2001, 2005));
    }

    [Fact]
    public void The_bar_is_limited_by_the_slots_of_the_level() {
        var player = Player();
        player.TryConsumeEssence(2001);
        player.TryConsumeEssence(2005);

        Assert.Null(player.TryEquipMimic(2001));
        Assert.Contains("No free mimicry slot", player.TryEquipMimic(2005));
        Assert.Contains("not learned", player.TryEquipMimic(2008));
        Assert.Null(player.TryUnequipMimic(2001));
        Assert.Null(player.TryEquipMimic(2005));
        Assert.True(new[] { 2005 }.SequenceEqual(player.EquippedMimicIds));
        Assert.Contains("not equipped", player.TryUnequipMimic(2001));
    }

    [Fact]
    public void A_higher_level_has_more_slots() {
        var player = Loaded(Saved(level: 11, mimics: [new MimicEntry(2001), new MimicEntry(2005)]));

        Assert.Equal(2, player.MimicSlotLimit);
        Assert.Null(player.TryEquipMimic(2001));
        Assert.Null(player.TryEquipMimic(2005));
    }

    [Fact]
    public void The_mimic_slots_stat_adds_slots() {
        var player = Player();

        player.SetSkillModifiers(1000, [new StatModifier(StatId.MimicSlots, ModifierKind.Flat, 2, 0)]);

        Assert.Equal(3, player.MimicSlotLimit);
    }

    [Fact]
    public void Casts_give_experience_and_levels() {
        var player = Player();
        player.TryConsumeEssence(2001);
        var threshold = TestSupport.ShippedMimic().Xp.Thresholds[0];

        for (var i = 0; i < threshold; i++) {
            player.RegisterMimicCast(2001);
        }

        player.RegisterMimicCast(2005);

        Assert.Equal(threshold, player.LearnedMimics.Single().Xp);
        Assert.Equal(2, player.MimicLevel(2001));
        Assert.Equal(0, player.MimicLevel(2005));
    }

    [Fact]
    public void Learned_abilities_equipment_and_experience_survive_a_save_and_load() {
        var player = Player();
        player.TryConsumeEssence(2001);
        player.TryConsumeEssence(2005);
        player.TryEquipMimic(2005);
        player.RegisterMimicCast(2005);
        player.RegisterMimicCast(2005);

        var state = player.CreatePersistenceState("aresden");
        var reloaded = Loaded(state.Noc!);

        Assert.Equal(CharacterSchema.Current, state.SchemaVersion);
        Assert.True(player.LearnedMimics.SequenceEqual(reloaded.LearnedMimics));
        Assert.True(new[] { 2005 }.SequenceEqual(reloaded.EquippedMimicIds));
        Assert.Equal(2, reloaded.LearnedMimics.Single(m => m.Id == 2005).Xp);
        Assert.True(state.Noc!.Mimics!.SequenceEqual(reloaded.CreatePersistenceState("aresden").Noc!.Mimics!));
    }

    [Fact]
    public void Loading_drops_unknown_duplicate_and_class_forbidden_abilities() {
        var player = Loaded(Saved(classId: 0, mimics: [new MimicEntry(9999), new MimicEntry(2000, 4), new MimicEntry(2001, 2), new MimicEntry(2001, 5)], equipped: [2000, 2001]));

        Assert.Equal(new MimicEntry(2001, 5), player.LearnedMimics.Single());
        Assert.True(new[] { 2001 }.SequenceEqual(player.EquippedMimicIds));
    }

    [Fact]
    public void Loading_trims_the_bar_to_the_slots_of_the_level() {
        var player = Loaded(Saved(mimics: [new MimicEntry(2001), new MimicEntry(2005)], equipped: [2005, 2001]));

        Assert.True(new[] { 2005 }.SequenceEqual(player.EquippedMimicIds));
        Assert.Equal(2, player.LearnedMimics.Count);
    }

    [Fact]
    public void Loading_caps_experience_and_never_refuses_the_save() {
        var player = Loaded(Saved(mimics: [new MimicEntry(2001, 9_999_999)]));

        Assert.Equal(TestSupport.ShippedMimic().Xp.MaxXp, player.LearnedMimics.Single().Xp);
        Assert.Equal(TestSupport.ShippedMimic().Xp.MaxLevel, player.MimicLevel(2001));
    }

    [Fact]
    public void A_state_without_mimicry_stays_without() {
        var state = Loaded(Saved()).CreatePersistenceState("aresden").Noc!;

        Assert.Null(state.Mimics);
        Assert.Null(state.MimicEquipped);
    }
}
