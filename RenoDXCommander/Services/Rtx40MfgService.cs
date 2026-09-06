using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages the RTX 40 MFG Unlock component — staging, install, and uninstall.
/// GitHub: dashdogy/RTX40MFG-Unlock. Deploys RTX40MFGCore.dll, RTX40MFG.asi,
/// and RTX40MFG-UI.addon64 to game folders alongside UAL and ReShade.
/// </summary>
public class Rtx40MfgService
{
    private const string GitHubApiUrl  = "https://api.github.com/repos/dashdogy/RTX40MFG-Unlock/releases/latest";
    private const string RepoUrl       = "https://github.com/dashdogy/RTX40MFG-Unlock";

    public const string AsiFileName    = "RTX40MFG.asi";
    public const string CoreDllName    = "RTX40MFGCore.dll";
    public const string AddonFileName  = "RTX40MFG-UI.addon64";

    private static readonly string[] DeployFiles = { AsiFileName, CoreDllName, AddonFileName };

    private readonly HttpClient     _http;
    private readonly ICrashReporter _crashReporter;
    private readonly string         _stagingDir;
    private readonly string         _versionFile;

    public Rtx40MfgService(HttpClient http, ICrashReporter crashReporter)
    {
        _http          = http;
        _crashReporter = crashReporter;
        _stagingDir    = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "rtx40mfg");
        _versionFile   = Path.Combine(_stagingDir, "version.txt");
    }

    public string? StagedVersion => File.Exists(_versionFile)
        ? File.ReadAllText(_versionFile).Trim() : null;

    public bool IsStagingReady =>
        File.Exists(Path.Combine(_stagingDir, AsiFileName)) &&
        File.Exists(Path.Combine(_stagingDir, CoreDllName));

    public bool HasUpdate      { get; private set; }
    public string? LatestVersion { get; private set; }

    /// <summary>Returns true if the component is installed in the given game folder.</summary>
    public static bool IsInstalled(string installPath) =>
        !string.IsNullOrEmpty(installPath) &&
        File.Exists(Path.Combine(installPath, AsiFileName));

    // ── Staging ───────────────────────────────────────────────────────────────

    public async Task EnsureStagingAsync()
    {
        if (IsStagingReady && !HasUpdate)
        {
            _crashReporter.Log("[Rtx40MfgService.EnsureStaging] Already up to date");
            return;
        }

        Directory.CreateDirectory(_stagingDir);

        var (version, downloadUrl) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(downloadUrl))
        {
            _crashReporter.Log("[Rtx40MfgService.EnsureStaging] Could not resolve latest release");
            return;
        }

        try
        {
            var bytes = await _http.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

            foreach (var entry in zip.Entries)
            {
                var name = Path.GetFileName(entry.FullName);
                if (string.IsNullOrEmpty(name)) continue;

                foreach (var target in DeployFiles)
                {
                    if (name.Equals(target, StringComparison.OrdinalIgnoreCase))
                    {
                        var dest = Path.Combine(_stagingDir, target);
                        using var src = entry.Open();
                        using var dst = File.Create(dest);
                        await src.CopyToAsync(dst).ConfigureAwait(false);
                        break;
                    }
                }
            }

            File.WriteAllText(_versionFile, version);
            HasUpdate = false;
            _crashReporter.Log($"[Rtx40MfgService.EnsureStaging] Staged {version}");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Rtx40MfgService.EnsureStaging] Download failed — {ex.Message}");
        }
    }

    public async Task CheckForUpdateAsync()
    {
        var (version, _) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version)) return;

        LatestVersion = version;
        var current = StagedVersion;
        HasUpdate = !string.Equals(current, version, StringComparison.OrdinalIgnoreCase);
        _crashReporter.Log($"[Rtx40MfgService.CheckForUpdate] Cached={current ?? "(none)"}, Remote={version}, HasUpdate={HasUpdate}");
    }

    private async Task<(string? version, string? downloadUrl)> FetchLatestReleaseInfoAsync()
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
            req.Headers.Add("User-Agent", "RHI");
            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return (null, null);

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            if (string.IsNullOrEmpty(tag)) return (null, null);

            if (!root.TryGetProperty("assets", out var assets)) return (null, null);
            foreach (var asset in assets.EnumerateArray())
            {
                var assetName = asset.TryGetProperty("name", out var np) ? np.GetString() : null;
                if (assetName?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true
                    && !assetName.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                {
                    var url = asset.TryGetProperty("browser_download_url", out var up) ? up.GetString() : null;
                    return (tag, url);
                }
            }
            return (tag, null);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Rtx40MfgService.FetchRelease] {ex.Message}");
            return (null, null);
        }
    }

    // ── Install ───────────────────────────────────────────────────────────────

    public bool Install(string installPath)
    {
        if (string.IsNullOrEmpty(installPath) || !IsStagingReady) return false;

        try
        {
            foreach (var file in DeployFiles)
            {
                var src  = Path.Combine(_stagingDir, file);
                if (!File.Exists(src)) continue;
                var dest = Path.Combine(installPath, file);
                File.Copy(src, dest, overwrite: true);
            }
            _crashReporter.Log($"[Rtx40MfgService.Install] Deployed to '{installPath}'");
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Rtx40MfgService.Install] Failed — {ex.Message}");
            return false;
        }
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────

    public void Uninstall(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return;

        try
        {
            foreach (var file in DeployFiles)
            {
                var path = Path.Combine(installPath, file);
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
            _crashReporter.Log($"[Rtx40MfgService.Uninstall] Removed from '{installPath}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Rtx40MfgService.Uninstall] Failed — {ex.Message}");
        }
    }
}
