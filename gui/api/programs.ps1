function Invoke-programsAction([string]$action, $argsObj) {
    switch ($action) {
        'installed' { return Get-InstalledPrograms }
        'uninstall' { return Invoke-UninstallProgram $argsObj }
        'leftovers' { return Find-Leftovers ([string]$argsObj.name) ([string]$argsObj.publisher) }
        'quarantine_move' { return Move-ToQuarantine $argsObj.paths ([string]$argsObj.appName) }
        'quarantine_list' { return Get-QuarantineList }
        'quarantine_restore' { return Restore-QuarantineItem ([string]$argsObj.id) }
        'quarantine_delete' { return Remove-QuarantineItem ([string]$argsObj.id) }
        'winget_updates' { return Get-WingetUpdates }
        'winget_upgrade' { return Invoke-WingetUpgrade ([string]$argsObj.id) }
        'hangar_catalog' { return Get-HangarCatalog }
        'hangar_search' { return Search-Winget ([string]$argsObj.query) }
        'hangar_install' { return Install-HangarApps $argsObj.ids }
        default { return [pscustomobject]@{ ok = $false; error = "Unknown action: $action" } }
    }
}

function Get-InstalledPrograms {
    $paths = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    $seen = @{}
    $list = @()
    foreach ($p in $paths) {
        Get-ItemProperty $p -ErrorAction SilentlyContinue | ForEach-Object {
            if (-not $_.DisplayName) { return }
            if ($_.SystemComponent -eq 1) { return }
            $key = ($_.DisplayName + '|' + $_.DisplayVersion).ToLowerInvariant()
            if ($seen.ContainsKey($key)) { return }
            $seen[$key] = $true
            $sizeMB = if ($_.EstimatedSize) { [math]::Round($_.EstimatedSize / 1024.0, 1) } else { 0 }
            $iconPath = [string]$_.DisplayIcon
            if (-not $iconPath -and $_.InstallLocation -and (Test-Path -LiteralPath $_.InstallLocation)) {
                $exe = Get-ChildItem -Path $_.InstallLocation -Filter '*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($exe) { $iconPath = $exe.FullName }
            }
            if (-not $iconPath -and $_.UninstallString) {
                $u = [string]$_.UninstallString
                if ($u -match '^"([^"]+\.exe)"' -or $u -match '^([a-zA-Z]:\\[^\s"]+\.exe)') {
                    if ($Matches[1] -notmatch 'msiexec\.exe') { $iconPath = $Matches[1] }
                }
            }
            $list += [pscustomobject]@{
                name = $_.DisplayName
                version = [string]$_.DisplayVersion
                publisher = [string]$_.Publisher
                installDate = [string]$_.InstallDate
                sizeMB = $sizeMB
                uninstall = [string]$_.UninstallString
                quietUninstall = [string]$_.QuietUninstallString
                installLocation = [string]$_.InstallLocation
                icon = $iconPath
                type = 'Win32'
            }
        }
    }
    $list = @($list | Sort-Object name)
    return [pscustomobject]@{ ok = $true; count = $list.Count; items = $list }
}

function Invoke-UninstallProgram($argsObj) {
    $cmd = if ($argsObj.silent -and $argsObj.quietUninstall) { [string]$argsObj.quietUninstall } else { [string]$argsObj.uninstall }
    if (-not $cmd) { return [pscustomobject]@{ ok = $false; error = 'No uninstall command available' } }
    if ($cmd -match '^msiexec(\.exe)?\s+(.*)$') {
        $rest = $Matches[2]
        if ($argsObj.silent -and $rest -notmatch '/q') { $rest = ($rest -replace '/I', '/X') + ' /qn /norestart' }
        Start-Process 'msiexec.exe' -ArgumentList $rest -Wait
        return [pscustomobject]@{ ok = $true }
    }
    if ($cmd.StartsWith('"')) {
        $idx = $cmd.IndexOf('"', 1)
        if ($idx -gt 1) {
            $exe = $cmd.Substring(1, $idx - 1)
            $arg = $cmd.Substring($idx + 1).Trim()
            if (Test-Path -LiteralPath $exe) {
                if ($arg) { Start-Process -FilePath $exe -ArgumentList $arg -Wait } else { Start-Process -FilePath $exe -Wait }
                return [pscustomobject]@{ ok = $true }
            }
        }
    }
    Start-Process 'cmd.exe' -ArgumentList ('/c ' + $cmd) -Wait
    return [pscustomobject]@{ ok = $true }
}

function Find-Leftovers([string]$name, [string]$publisher) {
    $tokens = @()
    foreach ($raw in @($name, $publisher)) {
        if (-not $raw) { continue }
        $clean = ($raw -replace '\b(Inc|Corp|Corporation|Ltd|LLC|GmbH|Co|Software|Technologies)\b', '' -replace '[^a-zA-Z0-9 ]', ' ').Trim()
        if ($clean.Length -ge 4) { $tokens += $clean }
        $first = ($clean -split '\s+')[0]
        if ($first -and $first.Length -ge 4) { $tokens += $first }
    }
    $tokens = @($tokens | Select-Object -Unique)
    if ($tokens.Count -eq 0) { return [pscustomobject]@{ ok = $true; items = @() } }

    $roots = @($env:LOCALAPPDATA, $env:APPDATA, $env:ProgramData)
    $found = @()
    foreach ($r in $roots) {
        if (-not $r -or -not (Test-Path -LiteralPath $r)) { continue }
        Get-ChildItem -Path $r -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $dn = $_.Name
            foreach ($tk in $tokens) {
                if ($dn -like "*$tk*") {
                    $sz = 0
                    try {
                        $sz = (Get-ChildItem -Path $_.FullName -File -Recurse -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
                    } catch { }
                    $found += [pscustomobject]@{
                        path = $_.FullName
                        kind = 'Folder'
                        reason = "Matches '$tk'"
                        sizeMB = [math]::Round($sz / 1MB, 2)
                    }
                    break
                }
            }
        }
    }
    return [pscustomobject]@{ ok = $true; items = $found }
}

function Move-ToQuarantine($paths, [string]$appName) {
    $qRoot = Join-Path $script:WhaSettingsDir 'quarantine'
    $id = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + ([guid]::NewGuid().ToString('N').Substring(0,6))
    $qDir = Join-Path $qRoot $id
    New-Item -ItemType Directory -Path $qDir -Force | Out-Null
    $manifest = @()
    foreach ($p in @($paths)) {
        if (-not $p -or -not (Test-Path -LiteralPath $p)) { continue }
        $leaf = Split-Path -Leaf $p
        $dest = Join-Path $qDir $leaf
        Move-Item -LiteralPath $p -Destination $dest -Force -ErrorAction SilentlyContinue
        $manifest += [pscustomobject]@{ original = $p; stored = $dest }
    }
    $meta = [pscustomobject]@{ id = $id; app = $appName; date = (Get-Date).ToString('yyyy-MM-dd HH:mm'); items = $manifest }
    $meta | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $qDir 'manifest.json') -Encoding UTF8
    return [pscustomobject]@{ ok = $true; id = $id; count = $manifest.Count }
}

function Get-QuarantineList {
    $qRoot = Join-Path $script:WhaSettingsDir 'quarantine'
    if (-not (Test-Path -LiteralPath $qRoot)) { return [pscustomobject]@{ ok = $true; items = @() } }
    $items = @()
    Get-ChildItem -Path $qRoot -Directory -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | ForEach-Object {
        $mf = Join-Path $_.FullName 'manifest.json'
        if (Test-Path -LiteralPath $mf) {
            try { $items += (Get-Content $mf -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { }
        }
    }
    return [pscustomobject]@{ ok = $true; items = $items }
}

function Restore-QuarantineItem([string]$id) {
    $qDir = Join-Path (Join-Path $script:WhaSettingsDir 'quarantine') $id
    $mf = Join-Path $qDir 'manifest.json'
    if (-not (Test-Path -LiteralPath $mf)) { return [pscustomobject]@{ ok = $false; error = 'Not found' } }
    $meta = Get-Content $mf -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($it in @($meta.items)) {
        if (Test-Path -LiteralPath $it.stored) {
            $parent = Split-Path -Parent $it.original
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
            Move-Item -LiteralPath $it.stored -Destination $it.original -Force -ErrorAction SilentlyContinue
        }
    }
    Remove-Item -LiteralPath $qDir -Recurse -Force -ErrorAction SilentlyContinue
    return [pscustomobject]@{ ok = $true }
}

function Remove-QuarantineItem([string]$id) {
    $qDir = Join-Path (Join-Path $script:WhaSettingsDir 'quarantine') $id
    if (Test-Path -LiteralPath $qDir) {
        Remove-Item -LiteralPath $qDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    return [pscustomobject]@{ ok = $true }
}

function Get-WingetUpdates {
    $wg = Get-Command 'winget.exe' -ErrorAction SilentlyContinue
    if (-not $wg) { return [pscustomobject]@{ ok = $false; error = 'winget is not installed'; items = @() } }
    if (Get-Command Write-JobLog -ErrorAction SilentlyContinue) { Write-JobLog 'Running winget upgrade scan...' }
    $raw = & $wg.Source upgrade --accept-source-agreements 2>&1 | Out-String
    $lines = $raw -split "`r?`n"
    $items = @()
    $sepIdx = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^-{5,}') { $sepIdx = $i; break }
    }
    if ($sepIdx -ge 1) {
        for ($i = $sepIdx + 1; $i -lt $lines.Count; $i++) {
            $ln = $lines[$i].TrimEnd()
            if (-not $ln -or $ln -match 'upgrades available' -or $ln -match '^\d+\s+package') { continue }
            $cols = $ln -split '\s{2,}'
            if ($cols.Count -ge 4) {
                $items += [pscustomobject]@{
                    name = $cols[0].Trim()
                    id = $cols[1].Trim()
                    version = $cols[2].Trim()
                    available = $cols[3].Trim()
                    source = if ($cols.Count -ge 5) { $cols[4].Trim() } else { 'winget' }
                }
            }
        }
    }
    return [pscustomobject]@{ ok = $true; count = $items.Count; items = $items }
}

function Invoke-WingetUpgrade([string]$id) {
    $wg = Get-Command 'winget.exe' -ErrorAction SilentlyContinue
    if (-not $wg) { return [pscustomobject]@{ ok = $false; error = 'winget not found' } }
    if (Get-Command Write-JobLog -ErrorAction SilentlyContinue) { Write-JobLog "Updating $id..." }
    $argList = if ($id -eq '__ALL__') {
        @('upgrade', '--all', '--silent', '--accept-package-agreements', '--accept-source-agreements')
    } else {
        @('upgrade', '--id', $id, '-e', '--silent', '--accept-package-agreements', '--accept-source-agreements')
    }
    $out = & $wg.Source @argList 2>&1
    if (Get-Command Write-JobLog -ErrorAction SilentlyContinue) {
        foreach ($l in $out) { if ($l) { Write-JobLog ([string]$l) } }
    }
    return [pscustomobject]@{ ok = ($LASTEXITCODE -eq 0); exitCode = $LASTEXITCODE }
}

function Get-HangarCatalog {
    $catalog = @(
        @{ id='7zip.7zip'; name='7-Zip'; cat='Utilities'; kit='Essentials'; desc='High-compression file archiver' },
        @{ id='Mozilla.Firefox'; name='Firefox'; cat='Browser'; kit='Essentials'; desc='Privacy-focused web browser' },
        @{ id='VideoLAN.VLC'; name='VLC Media Player'; cat='Media'; kit='Essentials'; desc='Universal media player' },
        @{ id='Notepad++.Notepad++'; name='Notepad++'; cat='Utilities'; kit='Essentials'; desc='Fast lightweight text & code editor' },
        @{ id='voidtools.Everything'; name='Everything'; cat='Utilities'; kit='Essentials'; desc='Instant NTFS file search' },
        @{ id='ShareX.ShareX'; name='ShareX'; cat='Utilities'; kit='Essentials'; desc='Screenshot & screen recording tool' },
        @{ id='Bitwarden.Bitwarden'; name='Bitwarden'; cat='Security'; kit='Essentials'; desc='Open-source password manager' },
        @{ id='Microsoft.PowerToys'; name='PowerToys'; cat='Utilities'; kit='Essentials'; desc='Windows power user utilities' },
        @{ id='Git.Git'; name='Git'; cat='Developer'; kit='Developer'; desc='Distributed version control' },
        @{ id='Microsoft.VisualStudioCode'; name='Visual Studio Code'; cat='Developer'; kit='Developer'; desc='Extensible code editor' },
        @{ id='Python.Python.3.12'; name='Python 3.12'; cat='Developer'; kit='Developer'; desc='Python runtime and pip' },
        @{ id='OpenJS.NodeJS.LTS'; name='Node.js LTS'; cat='Developer'; kit='Developer'; desc='JavaScript runtime' },
        @{ id='Microsoft.WindowsTerminal'; name='Windows Terminal'; cat='Developer'; kit='Developer'; desc='Modern tabbed terminal' },
        @{ id='Docker.DockerDesktop'; name='Docker Desktop'; cat='Developer'; kit='Developer'; desc='Container development environment' },
        @{ id='Valve.Steam'; name='Steam'; cat='Gaming'; kit='Gamer'; desc='PC gaming platform' },
        @{ id='Discord.Discord'; name='Discord'; cat='Communication'; kit='Gamer'; desc='Voice, video and text chat' },
        @{ id='OBSProject.OBSStudio'; name='OBS Studio'; cat='Media'; kit='Creator'; desc='Streaming and screen recording' },
        @{ id='Guru3D.Afterburner'; name='MSI Afterburner'; cat='Hardware'; kit='Gamer'; desc='GPU monitoring & overclocking' },
        @{ id='CPUID.CPU-Z'; name='CPU-Z'; cat='Hardware'; kit='Utilities'; desc='Detailed CPU & memory diagnostics' },
        @{ id='TechPowerUp.GPU-Z'; name='GPU-Z'; cat='Hardware'; kit='Utilities'; desc='Detailed graphics card telemetry' },
        @{ id='CrystalDewWorld.CrystalDiskInfo'; name='CrystalDiskInfo'; cat='Hardware'; kit='Utilities'; desc='SMART disk health monitor' },
        @{ id='GIMP.GIMP'; name='GIMP'; cat='Media'; kit='Creator'; desc='Image editor' },
        @{ id='BlenderFoundation.Blender'; name='Blender'; cat='Media'; kit='Creator'; desc='3D creation suite' },
        @{ id='HandBrake.HandBrake'; name='HandBrake'; cat='Media'; kit='Creator'; desc='Open-source video transcoder' },
        @{ id='SumatraPDF.SumatraPDF'; name='SumatraPDF'; cat='Utilities'; kit='Essentials'; desc='Fast minimal PDF & eBook reader' },
        @{ id='Rufus.Rufus'; name='Rufus'; cat='Utilities'; kit='Utilities'; desc='Bootable USB drive creator' },
        @{ id='WireGuard.WireGuard'; name='WireGuard'; cat='Security'; kit='Utilities'; desc='Fast modern VPN client' },
        @{ id='LocalSend.LocalSend'; name='LocalSend'; cat='Utilities'; kit='Essentials'; desc='Share files over local network' }
    )
    return [pscustomobject]@{ ok = $true; items = $catalog }
}

function Search-Winget([string]$q) {
    $wg = Get-Command 'winget.exe' -ErrorAction SilentlyContinue
    if (-not $wg -or -not $q) { return [pscustomobject]@{ ok = $false; items = @() } }
    $raw = & $wg.Source search $q --accept-source-agreements 2>&1 | Out-String
    $lines = $raw -split "`r?`n"
    $items = @()
    $sepIdx = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^-{5,}') { $sepIdx = $i; break } }
    if ($sepIdx -ge 1) {
        for ($i = $sepIdx + 1; $i -lt $lines.Count; $i++) {
            $cols = $lines[$i].TrimEnd() -split '\s{2,}'
            if ($cols.Count -ge 3) {
                $items += [pscustomobject]@{
                    name = $cols[0].Trim()
                    id = $cols[1].Trim()
                    version = $cols[2].Trim()
                }
            }
            if ($items.Count -ge 25) { break }
        }
    }
    return [pscustomobject]@{ ok = $true; items = $items }
}

function Install-HangarApps($ids) {
    $wg = Get-Command 'winget.exe' -ErrorAction SilentlyContinue
    if (-not $wg) { return [pscustomobject]@{ ok = $false; error = 'winget not found' } }
    $done = 0
    foreach ($id in @($ids)) {
        if (-not $id) { continue }
        if (Get-Command Write-JobLog -ErrorAction SilentlyContinue) { Write-JobLog "Installing $id..." }
        $out = & $wg.Source install --id $id -e --silent --accept-package-agreements --accept-source-agreements 2>&1
        if (Get-Command Write-JobLog -ErrorAction SilentlyContinue) {
            foreach ($l in $out) { if ($l) { Write-JobLog ([string]$l) } }
        }
        $done++
    }
    return [pscustomobject]@{ ok = $true; installed = $done }
}
