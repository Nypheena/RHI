# fetch-wiki-snapshot.ps1
# Fetches the RenoDX wiki Mods page and produces a JSON snapshot of all game mods.
# Fields match exactly what RHI's WikiService.cs parses:
#   name, maintainer, snapshotUrl, snapshotUrl32, nexusUrl, discordUrl,
#   status, notes, nameUrl
# Output: docs\renodx-wiki-snapshot.json

$WikiUrl  = "https://github.com/clshortfuse/renodx/wiki/Mods"
$OutFile  = Join-Path $PSScriptRoot "renodx-wiki-snapshot.json"

Write-Host "Fetching $WikiUrl ..."
$html = (Invoke-WebRequest -Uri $WikiUrl -UseBasicParsing).Content

# ── Minimal HTML helpers ──────────────────────────────────────────────────────

function Strip-Tags([string]$html) {
    $s = [System.Text.RegularExpressions.Regex]::Replace($html, '<br\s*/?>', "`n", 'IgnoreCase')
    $s = [System.Text.RegularExpressions.Regex]::Replace($s, '<li[^>]*>', "`n• ", 'IgnoreCase')
    $s = [System.Text.RegularExpressions.Regex]::Replace($s, '<[^>]+>', '')
    $s = [System.Web.HttpUtility]::HtmlDecode($s)
    # Normalize smart quotes → straight quotes
    $s = $s -replace '\u2018|\u2019', "'" -replace '\u201C|\u201D', '"'
    return $s.Trim()
}

function Get-CellText([string]$cellHtml) {
    return Strip-Tags $cellHtml
}

function Get-FirstHref([string]$cellHtml, [string]$pattern) {
    $matches = [System.Text.RegularExpressions.Regex]::Matches($cellHtml, 'href="([^"]+)"')
    foreach ($m in $matches) {
        $href = $m.Groups[1].Value
        if ($href -match $pattern) { return $href }
    }
    return $null
}

function Get-SnapshotUrl([string]$cellHtml) {
    $matches = [System.Text.RegularExpressions.Regex]::Matches($cellHtml, 'href="([^"]+)"')
    $url64 = $null; $url32 = $null
    foreach ($m in $matches) {
        $href = $m.Groups[1].Value
        if ($href -match '\.addon32$') { $url32 = $href }
        elseif ($href -match '\.addon64$|\.github\.io/renodx/|/releases/download/') { $url64 = $href }
        elseif ($href -match 'snapshot|download' -and -not $url64) { $url64 = $href }
    }
    return $url64, $url32
}

# ── Load HTML with HtmlAgilityPack if available, else use regex ───────────────
# We use regex since this is a standalone script with no dependencies.

# ── Find the deprecated heading position ─────────────────────────────────────
$deprecatedPos = -1
$depMatch = [System.Text.RegularExpressions.Regex]::Match(
    $html, '<h[1-3][^>]*>[^<]*deprecated[^<]*</h[1-3]>', 'IgnoreCase')
if ($depMatch.Success) { $deprecatedPos = $depMatch.Index }

# ── Extract all tables ────────────────────────────────────────────────────────
$tablePattern = '(?s)<table[^>]*>(.*?)</table>'
$tableMatches = [System.Text.RegularExpressions.Regex]::Matches($html, $tablePattern, 'IgnoreCase')

$mods = [System.Collections.Generic.List[hashtable]]::new()
$genericNotes = @{}

foreach ($tableMatch in $tableMatches) {
    # Skip tables after the deprecated heading
    if ($deprecatedPos -ge 0 -and $tableMatch.Index -gt $deprecatedPos) {
        Write-Host "  Skipping table after 'Deprecated' heading"
        continue
    }

    $tableHtml = $tableMatch.Groups[1].Value

    # Extract header row
    $headerRowMatch = [System.Text.RegularExpressions.Regex]::Match($tableHtml, '(?s)<tr[^>]*>(.*?)</tr>', 'IgnoreCase')
    if (-not $headerRowMatch.Success) { continue }
    $headerHtml = $headerRowMatch.Groups[1].Value
    $headerCells = [System.Text.RegularExpressions.Regex]::Matches($headerHtml, '(?s)<t[hd][^>]*>(.*?)</t[hd]>', 'IgnoreCase')
    if ($headerCells.Count -lt 2) { continue }

    $headers = @($headerCells | ForEach-Object { (Get-CellText $_.Groups[1].Value).ToLowerInvariant() })

    $hasLinks  = $headers | Where-Object { $_ -match 'link|download' }
    $hasStatus = $headers | Where-Object { $_ -match 'status' }

    # Data rows (skip header row)
    $rowMatches = [System.Text.RegularExpressions.Regex]::Matches($tableHtml, '(?s)<tr[^>]*>(.*?)</tr>', 'IgnoreCase')

    if ($hasLinks -or ($hasStatus -and $headerCells.Count -ge 3)) {
        # ── Mod table ────────────────────────────────────────────────────────

        # Determine column indices
        $nameCol = 0; $maintainerCol = -1; $linksCol = -1; $statusCol = -1
        for ($i = 0; $i -lt $headers.Count; $i++) {
            $h = $headers[$i]
            if ($h -match 'maintainer|author|developer') { $maintainerCol = $i }
            elseif ($h -match 'link|download')           { $linksCol = $i }
            elseif ($h -match 'status')                  { $statusCol = $i }
        }

        Write-Host "  Mod table: $($headerCells.Count) cols [$($headers -join '|')] name=$nameCol maint=$maintainerCol links=$linksCol status=$statusCol"

        $isFirst = $true
        foreach ($rowMatch in $rowMatches) {
            if ($isFirst) { $isFirst = $false; continue }  # skip header row

            $rowHtml = $rowMatch.Groups[1].Value
            $cells = [System.Text.RegularExpressions.Regex]::Matches($rowHtml, '(?s)<td[^>]*>(.*?)</td>', 'IgnoreCase')
            if ($cells.Count -lt 2) { continue }

            $cellArr = @($cells | ForEach-Object { $_.Groups[1].Value })

            # Name
            $name = Get-CellText $cellArr[$nameCol]
            if ([string]::IsNullOrWhiteSpace($name)) { continue }

            # NameUrl — link in the name cell
            $nameUrl = $null
            $nameHrefMatch = [System.Text.RegularExpressions.Regex]::Match($cellArr[$nameCol], 'href="([^"]+)"')
            if ($nameHrefMatch.Success) {
                $href = $nameHrefMatch.Groups[1].Value
                if ($href -match '^http') { $nameUrl = $href }
                elseif ($href -match '^/') { $nameUrl = "https://github.com$href" }
                else { $nameUrl = "https://github.com/clshortfuse/renodx/wiki/" + $href.TrimStart('.','/')}
            }

            # Maintainer
            $maintainer = ""
            if ($maintainerCol -ge 0 -and $maintainerCol -lt $cellArr.Count) {
                $maintainer = Get-CellText $cellArr[$maintainerCol]
            }

            # Snapshot + Nexus + Discord URLs
            # If dedicated links column exists use it, otherwise scan all cells
            $snapshotUrl = $null; $snapshotUrl32 = $null; $nexusUrl = $null; $discordUrl = $null
            $scanCells = if ($linksCol -ge 0) { @($cellArr[$linksCol]) } else { $cellArr }
            foreach ($c in $scanCells) {
                $hrefMatches = [System.Text.RegularExpressions.Regex]::Matches($c, 'href="([^"]+)"')
                foreach ($hm in $hrefMatches) {
                    $href = $hm.Groups[1].Value
                    $anchorText = [System.Text.RegularExpressions.Regex]::Match($c, '<a[^>]+href="' + [regex]::Escape($href) + '"[^>]*>(.*?)</a>').Groups[1].Value.ToLowerInvariant()
                    if ($href -match '\.addon32$') { $snapshotUrl32 = $href }
                    elseif ($href -match '\.addon64$|\.github\.io/renodx/|/releases/download/') { $snapshotUrl = $href }
                    elseif ($href -match 'nexusmods\.com') { $nexusUrl = $href }
                    elseif ($href -match 'discord\.com|discord\.gg') { $discordUrl = $href }
                    elseif ($anchorText -match 'snapshot|download' -and -not $snapshotUrl) { $snapshotUrl = $href }
                }
            }
            # Promote 32-bit if no 64-bit
            if (-not $snapshotUrl -and $snapshotUrl32) { $snapshotUrl = $snapshotUrl32 }

            # Status + notes
            $status = "✅"; $notes = $null
            if ($statusCol -ge 0 -and $statusCol -lt $cellArr.Count) {
                $statusText = Get-CellText $cellArr[$statusCol]
                if ($statusText -match '🚧') { $status = "🚧" }
                # Tooltip notes
                $tooltipMatch = [System.Text.RegularExpressions.Regex]::Match($cellArr[$statusCol], 'title="([^"]+)"')
                if ($tooltipMatch.Success) { $notes = $tooltipMatch.Groups[1].Value.Trim() }
                # Notes after stripping emoji
                if (-not $notes) {
                    $afterEmoji = $statusText -replace '✅|🚧', '' | ForEach-Object { $_.Trim() }
                    if ($afterEmoji) { $notes = $afterEmoji }
                }
            }

            # Fallback: look for notes in extra cells
            if (-not $notes) {
                for ($i = 0; $i -lt $cellArr.Count; $i++) {
                    if ($i -eq $nameCol -or $i -eq $maintainerCol -or $i -eq $linksCol -or $i -eq $statusCol) { continue }
                    $cellNote = Get-CellText $cellArr[$i]
                    if (-not [string]::IsNullOrWhiteSpace($cellNote)) { $notes = $cellNote.Trim(); break }
                }
            }

            $mods.Add(@{
                name         = $name
                maintainer   = $maintainer
                snapshotUrl  = $snapshotUrl
                snapshotUrl32 = $snapshotUrl32
                nexusUrl     = $nexusUrl
                discordUrl   = $discordUrl
                status       = $status
                notes        = $notes
                nameUrl      = $nameUrl
            })
        }

    } else {
        # ── Generic notes table ──────────────────────────────────────────────
        $isFirst = $true
        foreach ($rowMatch in $rowMatches) {
            if ($isFirst) { $isFirst = $false; continue }
            $rowHtml = $rowMatch.Groups[1].Value
            $cells = [System.Text.RegularExpressions.Regex]::Matches($rowHtml, '(?s)<td[^>]*>(.*?)</td>', 'IgnoreCase')
            if ($cells.Count -lt 1) { continue }
            $cellArr = @($cells | ForEach-Object { $_.Groups[1].Value })
            $name = Get-CellText $cellArr[0]
            if ([string]::IsNullOrWhiteSpace($name)) { continue }
            $notesCell = if ($cellArr.Count -gt 2) { $cellArr[2] } elseif ($cellArr.Count -gt 1) { $cellArr[1] } else { $null }
            $noteText = if ($notesCell) { Get-CellText $notesCell } else { "" }
            if (-not [string]::IsNullOrWhiteSpace($noteText)) { $genericNotes[$name] = $noteText.Trim() }
        }
    }
}

# ── Build output ──────────────────────────────────────────────────────────────

$output = [ordered]@{
    generatedUtc  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    source        = $WikiUrl
    modCount      = $mods.Count
    mods          = $mods.ToArray()
    genericNotes  = $genericNotes
}

$json = $output | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText($OutFile, $json, [System.Text.Encoding]::UTF8)

Write-Host ""
Write-Host "Done. $($mods.Count) mods written to: $OutFile"
