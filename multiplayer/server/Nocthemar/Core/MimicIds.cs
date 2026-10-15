namespace Server.Nocthemar.Core;

/// <summary>
/// Mimicry ability id scheme: 2000-2099 are monster abilities (one per monster, <see cref="MonsterCount"/> slots), 2100-2199 are fusion results.
/// Legacy <c>Spells.json</c> ids are below 1000 and class skills are 1000-1999 (<see cref="SkillIds"/>), so an id alone says what it is.
/// A monster ability is learned from an essence item whose id is <see cref="EssenceItemBase"/> + (ability id - <see cref="Base"/>).
/// </summary>
public static class MimicIds {
    public const int Base = 2000;
    public const int MonsterCount = 100;
    public const int FusionBase = Base + MonsterCount;
    public const int FusionCount = 100;

    /// <summary>First id after the mimicry abilities.</summary>
    public const int End = FusionBase + FusionCount;

    /// <summary>Items.json id of the essence of the first monster ability.</summary>
    public const int EssenceItemBase = 5000;

    /// <summary>Items.json id of the Essence Vial: one is consumed to capture an essence orb (unless a capture passive does it).</summary>
    public const int EssenceVialItemId = 5100;

    public static bool IsMimic(int id) => id >= Base && id < End;

    public static bool IsMonsterAbility(int id) => id >= Base && id < FusionBase;

    public static bool IsFusion(int id) => id >= FusionBase && id < End;

    /// <summary>Items.json id of the essence of a monster ability; throws for any other id (fusions have no essence).</summary>
    public static int EssenceItemId(int abilityId) =>
        IsMonsterAbility(abilityId)
            ? EssenceItemBase + (abilityId - Base)
            : throw new ArgumentOutOfRangeException(nameof(abilityId), $"{abilityId} is not a monster ability id.");

    /// <summary>The monster ability an essence item stands for; false for any other item id.</summary>
    public static bool TryAbilityOfEssence(int itemId, out int abilityId) {
        var offset = itemId - EssenceItemBase;
        if (offset >= 0 && offset < MonsterCount) {
            abilityId = Base + offset;
            return true;
        }

        abilityId = 0;
        return false;
    }
}
