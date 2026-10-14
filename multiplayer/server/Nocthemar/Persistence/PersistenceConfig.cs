namespace Server.Nocthemar.Persistence;

/// <summary>Character persistence settings; JSON <c>persistence</c> in <c>Settings.json</c> (optional, defaults to the JSON file store).</summary>
/// <param name="Provider"><c>json</c> (default, <c>Chars/*.json</c>) or <c>pg</c> (PostgreSQL, not implemented yet).</param>
/// <param name="ConnectionString">Required by the <c>pg</c> provider only.</param>
public sealed record PersistenceConfig(string Provider = PersistenceConfig.JsonProvider, string? ConnectionString = null) {
    public const string JsonProvider = "json";
    public const string PgProvider = "pg";
}
