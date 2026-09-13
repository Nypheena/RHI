namespace RenoDXCommander.Models;

/// <summary>
/// Graphics API support information scraped from a PCGamingWiki page's
/// "Other information → API" section, plus config file location from the
/// "Game data → Configuration file(s)" section.
/// </summary>
public class PcgwApiInfo
{
    public bool HasDirectX9  { get; set; }
    public bool HasDirectX10 { get; set; }
    public bool HasDirectX11 { get; set; }
    public bool HasDirectX12 { get; set; }
    public bool HasVulkan    { get; set; }
    public bool HasOpenGL    { get; set; }
    public bool HasMetal     { get; set; }

    /// <summary>
    /// The Windows config file directory scraped from the PCGW "Game data → Configuration file(s)"
    /// section. Uses standard Windows environment variable syntax (e.g. %LOCALAPPDATA%\GameName\Saved\Config\Windows).
    /// Null if not found or not a Windows path. Passed directly to ResolveEngineIniDir as projectNameOverride.
    /// </summary>
    public string? ConfigPath { get; set; }
}
