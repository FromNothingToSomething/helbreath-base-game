using System.Text.RegularExpressions;
using Server.Nocthemar.Core;
using Xunit;

namespace NocthemarTests;

/// <summary>T25: append-only StatId, defaults of the new stats, and the SourceId convention.</summary>
public class StatIdTests {
    private static StatPipeline Create(int level = 1) => new(TestSupport.Progression(), level);

    // The numeric values are part of the contract (ids are append-only): this table must only ever grow.
    private static readonly Dictionary<string, int> Frozen = new() {
        ["MaxHp"] = 0, ["MaxMana"] = 1, ["ManaRegen"] = 2, ["DamageMult"] = 3,
        ["MagicDamageMult"] = 4, ["ArmorMult"] = 5, ["ResistPhysical"] = 6, ["ResistMagic"] = 7, ["ResistCurse"] = 8,
        ["ResistDeath"] = 9, ["CritDamageMult"] = 10, ["MoveSpeedMult"] = 11, ["HealReceivedMult"] = 12,
        ["XpGainMult"] = 13, ["ManaCostMult"] = 14, ["MimicDurationMult"] = 15, ["CritChance"] = 16, ["HpRegen"] = 17,
        ["MimicSlots"] = 18, ["MimicPowerMult"] = 19, ["MimicCostMult"] = 20,
    };

    [Fact]
    public void Existing_ids_are_unchanged_and_new_ones_are_appended() {
        foreach (var (name, value) in Frozen) {
            Assert.Equal(value, (int)Enum.Parse<StatId>(name));
        }

        Assert.Equal(Frozen.Count, Enum.GetValues<StatId>().Length);
    }

    [Fact]
    public void Count_matches_the_enum_and_ids_are_contiguous() {
        var values = Enum.GetValues<StatId>().Select(v => (int)v).OrderBy(v => v).ToArray();

        Assert.Equal(StatIds.Count, values.Length);
        Assert.Equal(Enumerable.Range(0, StatIds.Count), values);
    }

    [Fact]
    public void New_stats_have_neutral_defaults() {
        var p = Create();
        var multipliers = new[] {
            StatId.MagicDamageMult, StatId.ArmorMult, StatId.CritDamageMult, StatId.MoveSpeedMult,
            StatId.HealReceivedMult, StatId.XpGainMult, StatId.ManaCostMult, StatId.MimicDurationMult, StatId.MimicPowerMult, StatId.MimicCostMult,
        };
        var fractions = new[] { StatId.ResistPhysical, StatId.ResistMagic, StatId.ResistCurse, StatId.ResistDeath };

        foreach (var stat in multipliers) {
            Assert.Equal(1.0, p.Get(stat));
        }

        foreach (var stat in fractions) {
            Assert.Equal(0.0, p.Get(stat));
        }

        Assert.Equal(1.0, p.XpGainMult);
    }

    [Fact]
    public void Set_level_does_not_reset_new_stat_bases() {
        var p = Create();
        p.Add(new StatModifier(StatId.ArmorMult, ModifierKind.PercentAdd, 0.15, 7));

        p.SetLevel(30);

        Assert.Equal(1.15, p.Get(StatId.ArmorMult), 12);
    }

    [Fact]
    public void Modifiers_on_new_stats_follow_the_same_order_and_clamping() {
        var p = Create();
        p.Add(new StatModifier(StatId.ArmorMult, ModifierKind.PercentAdd, 0.15, 1));
        p.Add(new StatModifier(StatId.ResistPhysical, ModifierKind.Flat, 0.10, 1));
        p.Add(new StatModifier(StatId.MoveSpeedMult, ModifierKind.PercentMult, -0.10, 1));
        p.Add(new StatModifier(StatId.HealReceivedMult, ModifierKind.PercentAdd, -2.0, 1));

        Assert.Equal(1.15, p.Get(StatId.ArmorMult), 12);
        Assert.Equal(0.10, p.Get(StatId.ResistPhysical), 12);
        Assert.Equal(0.90, p.Get(StatId.MoveSpeedMult), 12);
        Assert.Equal(0.0, p.Get(StatId.HealReceivedMult)); // clamped, never negative
    }

    [Fact]
    public void New_stats_do_not_leak_into_the_existing_ones() {
        var before = Create();
        var after = Create();
        foreach (var stat in Enum.GetValues<StatId>().Where(s => (int)s >= 4)) {
            after.Add(new StatModifier(stat, ModifierKind.PercentAdd, 0.5, 9));
        }

        Assert.Equal(before.MaxHp, after.MaxHp);
        Assert.Equal(before.MaxMana, after.MaxMana);
        Assert.Equal(before.ManaRegen, after.ManaRegen);
        Assert.Equal(before.DamageMult, after.DamageMult);
        Assert.Equal(20, after.ComputeDamage(20));
    }

    [Fact]
    public void Source_ids_of_different_categories_never_collide() {
        Assert.NotEqual(ModifierSources.Race(3), ModifierSources.Class(3));
        Assert.NotEqual(ModifierSources.Class(3), ModifierSources.Buff(3));
        Assert.Equal((1 << 16) | 3, ModifierSources.Race(3));
        Assert.Equal(ModifierSourceCategory.Faction, ModifierSources.CategoryOf(ModifierSources.Faction(12)));
        Assert.Equal(12, ModifierSources.IdOf(ModifierSources.Faction(12)));
        Assert.Equal(ModifierSourceCategory.None, ModifierSources.CategoryOf(5)); // ad-hoc ids stay in category 0
    }

    [Fact]
    public void Source_id_range_is_checked() {
        Assert.Throws<ArgumentOutOfRangeException>(() => ModifierSources.Race(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ModifierSources.Race(ModifierSources.MaxId + 1));
        Assert.Equal((1 << 16) | ModifierSources.MaxId, ModifierSources.Race(ModifierSources.MaxId));
    }

    [Fact]
    public void Remove_source_removes_only_that_owner() {
        var p = Create();
        p.Add(new StatModifier(StatId.MaxHp, ModifierKind.PercentMult, 0.05, ModifierSources.Race(2)));
        p.Add(new StatModifier(StatId.ArmorMult, ModifierKind.PercentAdd, 0.15, ModifierSources.Race(2)));
        p.Add(new StatModifier(StatId.MaxHp, ModifierKind.PercentMult, 0.05, ModifierSources.Class(2)));

        Assert.Equal(2, p.RemoveSource(ModifierSources.Race(2)));

        Assert.Equal(1, p.ModifierCount);
        Assert.Equal(1.0, p.Get(StatId.ArmorMult));
        Assert.Equal(1050, p.MaxHp);
    }

    [Fact]
    public void Hot_path_with_all_stats_does_not_allocate() {
        var p = Create(5);
        for (var i = 0; i < StatIds.Count; i++) {
            p.Add(new StatModifier((StatId)i, (ModifierKind)(i % 3), 0.05, ModifierSources.Race(i)));
        }

        long sink = 0;
        for (var i = 0; i < 20_000; i++) {
            p.SetLevel(i % 100 + 1);
            sink += (long)(p.MaxHp + p.XpGainMult + p.Get(StatId.ArmorMult));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000_000; i++) {
            p.SetLevel(i % 100 + 1);
            sink += (long)(p.MaxHp + p.XpGainMult + p.Get(StatId.ArmorMult));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(sink != 0);
        Assert.True(allocated <= 16 * 1024, $"allocated {allocated} bytes over 1,000,000 calls");
    }

    [Fact]
    public void Every_stat_without_a_consumer_is_annotated_in_the_source() {
        var source = File.ReadAllText(FindStatPipelineSource());
        var withConsumer = new[] { StatId.MaxHp, StatId.MaxMana, StatId.ManaRegen, StatId.DamageMult };

        foreach (var stat in Enum.GetValues<StatId>().Except(withConsumer)) {
            var annotated = Regex.IsMatch(source, $@"// consumer: module \d[^\n]*\r?\n\s*{stat} = {(int)stat},");
            Assert.True(annotated, $"StatId.{stat} needs a '// consumer: module N' comment on the line above it.");
        }
    }

    private static string FindStatPipelineSource() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            var candidate = Path.Combine(dir.FullName, "server", "Nocthemar", "Core", "StatPipeline.cs");
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        throw new FileNotFoundException("multiplayer/server/Nocthemar/Core/StatPipeline.cs not found above " + AppContext.BaseDirectory);
    }
}
