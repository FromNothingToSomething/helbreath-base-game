using Server.Nocthemar.Data;

namespace Server.Nocthemar.Core;

/// <summary>Entry rules of the Nocthemar zones. Stateless; a world without a zone (the Helbreath worlds) is always open.</summary>
public sealed class ZoneService {
    private readonly ZoneConfig zones;

    public ZoneService(ZoneConfig zones) {
        this.zones = zones ?? throw new ArgumentNullException(nameof(zones));
    }

    public ZoneConfig Zones => zones;

    /// <summary>The refusal sent to a character that is below the zone's entry level.</summary>
    public static string LevelTooLow(ZoneDefinition zone) {
        ArgumentNullException.ThrowIfNull(zone);
        return $"You must be level {zone.MinLevel} to enter {zone.Name}.";
    }

    /// <summary>Null when a character of <paramref name="level"/> may enter the world <paramref name="worldId"/>, otherwise the refusal to show.</summary>
    public string? CheckEntry(string worldId, int level) =>
        zones.TryGet(worldId, out var zone) && level < zone.MinLevel ? LevelTooLow(zone) : null;
}
