using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public interface IRenoDXDbService
{
    /// <summary>
    /// Fetches both RenoDXdb.json (named mods) and RenoDXdb-unreal.json (UE-Extended games).
    /// Uses ETag caching — returns cached data on 304 Not Modified.
    /// </summary>
    Task<(List<GameMod> Mods, Dictionary<string, RenoDXDbUnrealEntry> UnrealEntries)> FetchAllAsync();

    /// <summary>Clears the ETag cache for both DB URLs so the next fetch is unconditional.</summary>
    void InvalidateCache();

    /// <summary>Named mod list from the last successful fetch. Empty until FetchAllAsync completes.</summary>
    IReadOnlyList<GameMod> CachedMods { get; }

    /// <summary>UE-Extended entry dict from the last successful fetch. Empty until FetchAllAsync completes.</summary>
    IReadOnlyDictionary<string, RenoDXDbUnrealEntry> CachedUnrealEntries { get; }
}
