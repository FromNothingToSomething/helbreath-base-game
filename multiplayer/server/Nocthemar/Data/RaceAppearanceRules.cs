using Server.World.Game;

namespace Server.Nocthemar.Data;

/// <summary>
/// T31: appearance changes are limited to what the player's race allows (<c>Races.json</c>, <c>appearance</c>).
/// The race itself is immutable: no client message changes it, it is fixed at character creation (T30).
/// A race that is not in the catalog (or no catalog) restricts nothing, so a legacy character is never locked out;
/// the shipped race 0, which legacy characters get, allows the whole domain (guarded by a test).
/// </summary>
public static class RaceAppearanceRules {
    public static bool IsAllowed(RaceConfig? races, int raceId, int gender, int skinColor, int hairStyle, int underwearColor) =>
        races is null || !races.TryGet(raceId, out var race) || race.Appearance.Allows(gender, skinColor, hairStyle, underwearColor);

    /// <summary>Applies the requested appearance when the player's race allows it. A refused request changes nothing and returns false.</summary>
    public static bool TryApply(GameWorldPlayer player, RaceConfig? races, int gender, int skinColor, int hairStyle, int underwearColor) {
        ArgumentNullException.ThrowIfNull(player);
        if (!IsAllowed(races, player.RaceId, gender, skinColor, hairStyle, underwearColor)) {
            return false;
        }

        player.SetAppearance(gender, skinColor, hairStyle, underwearColor);
        return true;
    }
}
