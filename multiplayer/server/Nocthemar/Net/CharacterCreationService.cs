using Mmorpg.Network;
using Server.Nocthemar.Data;

namespace Server.Nocthemar.Net;

/// <summary>A validated character creation choice: the race exists and allows the appearance.</summary>
public sealed record NewCharacterChoice(int RaceId, int Gender, int SkinColor, int HairStyleIndex, int UnderwearColorIndex);

/// <param name="Choice">Null when nothing is to be applied (default race and appearance).</param>
/// <param name="Refusal">Non-null: the login must be refused with this explicit message.</param>
public readonly record struct CharacterCreationResult(NewCharacterChoice? Choice, string? Refusal);

/// <summary>
/// T30: decides what <c>AuthenticateRequest.creation</c> means at login.
/// An existing save wins: the field is ignored (the race is immutable), even when it is invalid.
/// No creation block: nothing to apply, the character gets the default race and the default appearance.
/// An unknown race or an appearance the race does not allow is refused with a message naming the problem.
/// </summary>
public static class CharacterCreationService {
    public static CharacterCreationResult Resolve(NocCharacterCreation? creation, bool saveExists, RaceConfig races) {
        ArgumentNullException.ThrowIfNull(races);
        if (saveExists || creation is null) {
            return default;
        }

        if (!races.TryGet(creation.RaceId, out var race)) {
            return new(null, $"Character creation refused: unknown race {creation.RaceId}.");
        }

        var violation = race.Appearance.FirstViolation(creation.Gender, creation.SkinColor, creation.HairStyleIndex, creation.UnderwearColorIndex);
        if (violation is not null) {
            return new(null, $"Character creation refused: {violation} is not allowed for race '{race.Key}'.");
        }

        return new(new NewCharacterChoice(race.Id, creation.Gender, creation.SkinColor, creation.HairStyleIndex, creation.UnderwearColorIndex), null);
    }
}
