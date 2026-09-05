using System.Text.Json;
using System.Text.Json.Nodes;
using RHI.ManifestEditor.Models;

namespace RHI.ManifestEditor.Services;

/// <summary>
/// Handles loading and saving manifest.json, preserving _comment_ and _doc keys
/// that System.Text.Json would otherwise strip during round-trip deserialization.
/// Strategy: deserialize into RemoteManifest for editing, but keep the raw JsonNode
/// for keys we don't model (_comment_*, _doc), and merge on save.
/// </summary>
public class ManifestFileService
{
    private static readonly JsonSerializerOptions _readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions _finalWriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Loads a manifest from disk. Returns the parsed model plus the raw JsonObject for round-trip preservation.</summary>
    public (RemoteManifest manifest, JsonObject raw) Load(string path)
    {
        var json = File.ReadAllText(path);
        var manifest = JsonSerializer.Deserialize<RemoteManifest>(json, _readOptions)
            ?? throw new InvalidDataException("Failed to parse manifest.");
        var raw = JsonNode.Parse(json)!.AsObject();
        return (manifest, raw);
    }

    /// <summary>
    /// Saves the manifest back to disk. Re-serializes the typed model, then injects
    /// _comment_ and _doc keys from the original raw object to preserve section comments.
    /// </summary>
    public void Save(string path, RemoteManifest manifest, JsonObject originalRaw)
    {
        // Serialize the typed model
        var modelJson = JsonSerializer.Serialize(manifest, _writeOptions);
        var modelNode = JsonNode.Parse(modelJson)!.AsObject();

        // Build output: interleave comment keys from originalRaw before each real key
        var output = new JsonObject();

        // Always write version + _doc first
        output["version"] = manifest.Version;
        if (originalRaw.TryGetPropertyValue("_doc", out var docNode))
            output["_doc"] = docNode?.DeepClone();

        // Walk originalRaw key order — insert _comment_* inline, pull typed values from modelNode
        // Keys in this set are fully managed by the typed model — model output wins entirely,
        // no merging with raw (prevents raw-JSON preservation from overriding intentional deletions).
        var modelAuthoritativeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dlssPresets", "dlssPresetsDev", "featureFlags", "addonPacks"
        };

        foreach (var kv in originalRaw)
        {
            var key = kv.Key;
            if (key == "version" || key == "_doc") continue;

            if (key.StartsWith("_comment_") || key.StartsWith("_"))
            {
                output[key] = kv.Value?.DeepClone();
            }
            else if (modelNode.TryGetPropertyValue(key, out var modelVal))
            {
                // For fully-managed keys, use the model output directly.
                // For other object-typed values, merge modelNode with originalRaw to preserve
                // any nested keys the typed model doesn't capture.
                if (!modelAuthoritativeKeys.Contains(key) && modelVal is JsonObject modelObj && kv.Value is JsonObject rawObj)
                {
                    var merged = rawObj.DeepClone()!.AsObject();
                    foreach (var mkv in modelObj)
                    {
                        merged[mkv.Key] = mkv.Value?.DeepClone();
                    }
                    output[key] = merged;
                }
                else
                {
                    output[key] = modelVal?.DeepClone();
                }
            }
            else
            {
                // Key is in originalRaw but not in the typed model — preserve it as-is
                // so the editor never silently drops fields it doesn't understand.
                output[key] = kv.Value?.DeepClone();
            }
        }

        // Any keys in modelNode not yet in output (new fields added to the model)
        foreach (var kv in modelNode)
        {
            if (!output.ContainsKey(kv.Key))
                output[kv.Key] = kv.Value?.DeepClone();
        }

        var finalJson = output.ToJsonString(_finalWriteOptions);
        File.WriteAllText(path, finalJson);
    }

    /// <summary>Validates a manifest file as JSON. Returns null if valid, error message if not.</summary>
    public string? Validate(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            JsonNode.Parse(json);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Returns all game names referenced anywhere in the manifest.</summary>
    public static IEnumerable<string> GetAllGameNames(RemoteManifest m)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddList(IEnumerable<string>? list) { if (list != null) foreach (var s in list) names.Add(s); }
        void AddDictKeys(IEnumerable<string>? keys) { if (keys != null) foreach (var k in keys) names.Add(k); }

        AddList(m.Blacklist); AddList(m.WikiUnlinks); AddList(m.NativeHdrGames);
        AddList(m.UeExtendedGames); AddList(m.NoUeExtendedGames); AddList(m.LumaRenodxCompat);
        AddList(m.LumaDefaultGames); AddList(m.ThirtyTwoBitGames); AddList(m.SixtyFourBitGames);
        AddList(m.DlssSkipGames); AddList(m.DofFixForceGames); AddList(m.DofFixSkipGames);
        AddList(m.DxvkBlacklist); AddList(m.ProfileExeExclusions);

        AddDictKeys(m.WikiNameOverrides?.Keys); AddDictKeys(m.LumaNameOverrides?.Keys);
        AddDictKeys(m.InstallPathOverrides?.Keys); AddDictKeys(m.EngineOverrides?.Keys);
        AddDictKeys(m.EngineHintOverrides?.Keys); AddDictKeys(m.EngineIniPathOverrides?.Keys);
        AddDictKeys(m.GraphicsApiOverrides?.Keys); AddDictKeys(m.DllNameOverrides?.Keys);
        AddDictKeys(m.GacSymlinkGames?.Keys); AddDictKeys(m.LaunchExeOverrides?.Keys);
        AddDictKeys(m.SnapshotOverrides?.Keys); AddDictKeys(m.ForceExternalOnly?.Keys);
        AddDictKeys(m.GameNotes?.Keys); AddDictKeys(m.ReshadeGameInfo?.Keys);
        AddDictKeys(m.LumaGameNotes?.Keys); AddDictKeys(m.InstallWarnings?.Keys);
        AddDictKeys(m.RenodxIniOverrides?.Keys); AddDictKeys(m.LegacyReShadeVersions?.Keys);
        AddDictKeys(m.AuthorOverrides?.Keys); AddDictKeys(m.NexusUrlOverrides?.Keys);
        AddDictKeys(m.PcgwUrlOverrides?.Keys); AddDictKeys(m.UwFixUrlOverrides?.Keys);
        AddDictKeys(m.UltraPlusUrlOverrides?.Keys); AddDictKeys(m.OptiScalerWikiNames?.Keys);
        AddDictKeys(m.ProfileNameOverrides?.Keys); AddDictKeys(m.WikiStatusOverrides?.Keys);
        AddDictKeys(m.SplitGames?.Keys); AddDictKeys(m.UeExtendedCompatibility?.Keys);
        AddDictKeys(m.PdUpscalerGames?.Keys); AddDictKeys(m.SteamAppIdOverrides?.Keys);

        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Returns all fields that reference a specific game name.</summary>
    public static List<(string Field, string Section, string Value)> GetEntriesForGame(RemoteManifest m, string gameName)
    {
        var results = new List<(string, string, string)>();
        bool Matches(string? s) => string.Equals(s, gameName, StringComparison.OrdinalIgnoreCase);

        void CheckList(List<string>? list, string field, string section)
        {
            if (list?.Any(Matches) == true) results.Add((field, section, "✓ present"));
        }
        void CheckDict<T>(Dictionary<string, T>? dict, string field, string section)
        {
            if (dict == null) return;
            foreach (var kv in dict)
                if (Matches(kv.Key)) results.Add((field, section, kv.Value?.ToString() ?? ""));
        }

        CheckList(m.Blacklist, "blacklist", "Detection");
        CheckList(m.WikiUnlinks, "wikiUnlinks", "Detection");
        CheckList(m.NativeHdrGames, "nativeHdrGames", "UE/HDR");
        CheckList(m.UeExtendedGames, "ueExtendedGames", "UE/HDR");
        CheckList(m.NoUeExtendedGames, "noUeExtendedGames", "UE/HDR");
        CheckList(m.LumaRenodxCompat, "lumaRenodxCompat", "UE/HDR");
        CheckList(m.LumaDefaultGames, "lumaDefaultGames", "UE/HDR");
        CheckList(m.ThirtyTwoBitGames, "thirtyTwoBitGames", "Engine");
        CheckList(m.SixtyFourBitGames, "sixtyFourBitGames", "Engine");
        CheckList(m.DlssSkipGames, "dlssSkipGames", "Engine");
        CheckList(m.DofFixForceGames, "dofFixForceGames", "DOF Fix");
        CheckList(m.DofFixSkipGames, "dofFixSkipGames", "DOF Fix");
        CheckList(m.DxvkBlacklist, "dxvkBlacklist", "DXVK");

        CheckDict(m.WikiNameOverrides, "wikiNameOverrides", "Detection");
        CheckDict(m.LumaNameOverrides, "lumaNameOverrides", "Detection");
        CheckDict(m.InstallPathOverrides, "installPathOverrides", "Detection");
        CheckDict(m.SplitGames, "splitGames", "Detection");
        CheckDict(m.EngineOverrides, "engineOverrides", "Engine");
        CheckDict(m.EngineHintOverrides, "engineHintOverrides", "Engine");
        CheckDict(m.EngineIniPathOverrides, "engineIniPathOverrides", "Engine");
        CheckDict(m.GraphicsApiOverrides, "graphicsApiOverrides", "Engine");
        CheckDict(m.DllNameOverrides, "dllNameOverrides", "Install");
        CheckDict(m.GacSymlinkGames, "gacSymlinkGames", "Install");
        CheckDict(m.LaunchExeOverrides, "launchExeOverrides", "Install");
        CheckDict(m.SnapshotOverrides, "snapshotOverrides", "Install");
        CheckDict(m.LegacyReShadeVersions, "legacyReShadeVersions", "Install");
        CheckDict(m.RenodxIniOverrides, "renodxIniOverrides", "Install");
        CheckDict(m.ForceExternalOnly, "forceExternalOnly", "Install");
        CheckDict(m.InstallWarnings, "installWarnings", "Install");
        CheckDict(m.UeExtendedCompatibility, "ueExtendedCompatibility", "UE/HDR");
        CheckDict(m.GameNotes, "gameNotes", "Notes");
        CheckDict(m.ReshadeGameInfo, "reshadeGameInfo", "Notes");
        CheckDict(m.LumaGameNotes, "lumaGameNotes", "Notes");
        CheckDict(m.DxvkGameNotes, "dxvkGameNotes", "DXVK");
        CheckDict(m.AuthorOverrides, "authorOverrides", "Authors/URLs");
        CheckDict(m.NexusUrlOverrides, "nexusUrlOverrides", "Authors/URLs");
        CheckDict(m.PcgwUrlOverrides, "pcgwUrlOverrides", "Authors/URLs");
        CheckDict(m.UwFixUrlOverrides, "uwFixUrlOverrides", "Authors/URLs");
        CheckDict(m.UltraPlusUrlOverrides, "ultraPlusUrlOverrides", "Authors/URLs");
        CheckDict(m.OptiScalerWikiNames, "optiScalerWikiNames", "Authors/URLs");
        CheckDict(m.WikiStatusOverrides, "wikiStatusOverrides", "Wiki Status");
        CheckDict(m.ProfileNameOverrides, "profileNameOverrides", "NVIDIA/DLSS");
        CheckDict(m.PdUpscalerGames, "pdUpscalerGames", "Engine");
        CheckDict(m.SteamAppIdOverrides, "steamAppIdOverrides", "Detection");

        return results;
    }
}
