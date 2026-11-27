function Invoke-optimizerAction([string]$action, $argsObj) {
    switch ($action) {
        'clean_scan' { return Scan-CleanTargets }
        'clean_run' { return Run-CleanTargets $argsObj.ids }
        'startup_list' { return Get-StartupItems }
        'startup_toggle' { return Set-StartupItem $argsObj }
        'services_list' { return Get-ServicesList }
        'services_set' { return Set-ServiceState $argsObj }
        'perf_read' { return Get-PerfSettings }
        'perf_set' { return Set-PerfSetting $argsObj }
        'debloat_list' { return Get-DebloatList }
        'debloat_apply' { return Apply-DebloatTweak ([string]$argsObj.id) ([bool]$argsObj.enable) }
        'repair' { return Run-SystemRepair ([string]$argsObj.kind) }
        default { return [pscustomobject]@{ ok = $false; error = "Unknown action: $action" } }
    }
}

function Get-FolderSizeSafe([string]$p) {
    if (-not $p -or -not (Test-Path -LiteralPath $p)) { return [int64]0 }
    try {
        $sum = (Get-ChildItem -LiteralPath $p -File -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
        return [int64]$sum
    } catch { return [int64]0 }
}

function Scan-CleanTargets {
    $targets = @(
        @{ id='user_temp'; name='User Temp Files'; path=$env:TEMP; desc='Temporary files created by user applications' },
        @{ id='win_temp'; name='Windows Temp'; path="$env:SystemRoot\Temp"; desc='System-wide temporary files' },
        @{ id='prefetch'; name='Windows Prefetch'; path="$env:SystemRoot\Prefetch"; desc='Cached application launch traces' },
        @{ id='wu_cache'; name='Windows Update Cache'; path="$env:SystemRoot\SoftwareDistribution\Download"; desc='Downloaded Windows Update installers' },
        @{ id='crash_dumps'; name='Crash Dumps'; path="$env:LOCALAPPDATA\CrashDumps"; desc='Application crash minidumps' },
        @{ id='d3d_cache'; name='DirectX Shader Cache'; path="$env:LOCALAPPDATA\D3DSCache"; desc='Compiled GPU shader cache' },
        @{ id='thumb_cache'; name='Thumbnail Cache'; path="$env:LOCALAPPDATA\Microsoft\Windows\Explorer"; desc='Explorer icon & thumbnail cache files' }
    )
    $items = @()
    foreach ($t in $targets) {
        $bytes = Get-FolderSizeSafe $t.path
        $items += [pscustomobject]@{
            id = $t.id
            name = $t.name
            path = $t.path
            desc = $t.desc
            bytes = $bytes
            mb = [math]::Round($bytes / 1MB, 1)
        }
    }
    return [pscustomobject]@{ ok = $true; items = $items }
}

function Run-CleanTargets($ids) {
    $map = @{
        user_temp   = $env:TEMP
        win_temp    = "$env:SystemRoot\Temp"
        prefetch    = "$env:SystemRoot\Prefetch"
        wu_cache    = "$env:SystemRoot\SoftwareDistribution\Download"
        crash_dumps = "$env:LOCALAPPDATA\CrashDumps"
        d3d_cache   = "$env:LOCALAPPDATA\D3DSCache"
    }
    $removed = 0
    foreach ($id in @($ids)) {
        if (-not $map.ContainsKey($id)) { continue }
        $p = $map[$id]
        if (Test-Path -LiteralPath $p) {
            Get-ChildItem -LiteralPath $p -Force -ErrorAction SilentlyContinue | ForEach-Object {
                try {
                    Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop
                    $removed++
                } catch { }
            }
        }
    }
    return [pscustomobject]@{ ok = $true; removed = $removed }
}

function Get-StartupItems {
    $items = @()
    $regRoots = @(
        @{ path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'; scope='HKCU' },
        @{ path='HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'; scope='HKLM' }
    )
    foreach ($rr in $regRoots) {
        if (-not (Test-Path $rr.path)) { continue }
        $props = Get-ItemProperty $rr.path -ErrorAction SilentlyContinue
        $disPath = $rr.path -replace '\\Run$', '\Explorer\StartupApproved\Run'
        $disProps = if (Test-Path $disPath) { Get-ItemProperty $disPath -ErrorAction SilentlyContinue } else { $null }
        foreach ($p in $props.PSObject.Properties) {
            if ($p.Name -in @('PSPath','PSParentPath','PSChildName','PSDrive','PSProvider')) { continue }
            $enabled = $true
            if ($disProps -and $disProps.PSObject.Properties[$p.Name]) {
                $bytes = $disProps.($p.Name)
                if ($bytes -is [byte[]] -and $bytes.Length -ge 1 -and $bytes[0] -eq 3) { $enabled = $false }
            }
            $items += [pscustomobject]@{
                name = $p.Name
                command = [string]$p.Value
                scope = $rr.scope
                regPath = $rr.path
                enabled = $enabled
            }
        }
    }
    return [pscustomobject]@{ ok = $true; items = $items }
}

function Set-StartupItem($argsObj) {
    $name = [string]$argsObj.name
    $scope = [string]$argsObj.scope
    $enable = [bool]$argsObj.enabled
    $base = if ($scope -eq 'HKLM') { 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' } else { 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' }
    if (-not (Test-Path $base)) { New-Item -Path $base -Force | Out-Null }
    $val = if ($enable) { [byte[]](2,0,0,0,0,0,0,0,0,0,0,0) } else { [byte[]](3,0,0,0,0,0,0,0,0,0,0,0) }
    Set-ItemProperty -Path $base -Name $name -Value $val -Type Binary -ErrorAction Stop
    return [pscustomobject]@{ ok = $true }
}

function Get-ServicesList {
    $svcs = @(Get-Service -ErrorAction SilentlyContinue | Sort-Object DisplayName | ForEach-Object {
        [pscustomobject]@{
            name = $_.Name
            display = $_.DisplayName
            status = [string]$_.Status
            startType = [string]$_.StartType
        }
    })
    return [pscustomobject]@{ ok = $true; items = $svcs }
}

function Set-ServiceState($argsObj) {
    $name = [string]$argsObj.name
    $action = [string]$argsObj.op
    switch ($action) {
        'start'     { Start-Service -Name $name -ErrorAction Stop }
        'stop'      { Stop-Service -Name $name -Force -ErrorAction Stop }
        'auto'      { Set-Service -Name $name -StartupType Automatic -ErrorAction Stop }
        'manual'    { Set-Service -Name $name -StartupType Manual -ErrorAction Stop }
        'disabled'  { Set-Service -Name $name -StartupType Disabled -ErrorAction Stop }
    }
    return [pscustomobject]@{ ok = $true }
}

function Get-PerfSettings {
    $plan = ''
    try {
        $out = & powercfg.exe /getactivescheme 2>$null | Out-String
        if ($out -match '\(([^)]+)\)') { $plan = $Matches[1] }
    } catch { }

    $gameMode = $true
    try {
        $gm = Get-ItemProperty 'HKCU:\Software\Microsoft\GameBar' -ErrorAction SilentlyContinue
        if ($null -ne $gm.AllowAutoGameMode) { $gameMode = ($gm.AllowAutoGameMode -eq 1) }
    } catch { }

    $hags = $false
    try {
        $g = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' -ErrorAction SilentlyContinue
        if ($g.HwSchMode -eq 2) { $hags = $true }
    } catch { }

    return [pscustomobject]@{
        ok = $true
        powerPlan = $plan
        gameMode = $gameMode
        hags = $hags
    }
}

function Set-PerfSetting($argsObj) {
    $key = [string]$argsObj.key
    $val = $argsObj.value
    switch ($key) {
        'powerPlan' {
            $guid = switch ($val) {
                'balanced' { '381b4222-f694-41f0-9685-ff5bb260df2e' }
                'high'     { '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c' }
                'power'    { 'a1841308-3541-4fab-bc81-f71556f20b4a' }
                default    { '' }
            }
            if ($guid) { & powercfg.exe /setactive $guid | Out-Null }
        }
        'gameMode' {
            $p = 'HKCU:\Software\Microsoft\GameBar'
            if (-not (Test-Path $p)) { New-Item -Path $p -Force | Out-Null }
            Set-ItemProperty -Path $p -Name 'AllowAutoGameMode' -Value $(if ($val) { 1 } else { 0 }) -Type DWord
        }
        'hags' {
            $p = 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers'
            Set-ItemProperty -Path $p -Name 'HwSchMode' -Value $(if ($val) { 2 } else { 1 }) -Type DWord -ErrorAction Stop
        }
    }
    return [pscustomobject]@{ ok = $true }
}

function Get-DebloatList {
    $tweaks = @(
        @{ id='bing_search'; name='Disable Bing in Start Search'; desc='Stops Start menu from sending local searches to Bing'; reg='HKCU:\Software\Policies\Microsoft\Windows\Explorer'; prop='DisableSearchBoxSuggestions'; onVal=1; offVal=0 },
        @{ id='copilot'; name='Disable Windows Copilot Button'; desc='Hides Copilot button and policy on taskbar'; reg='HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot'; prop='TurnOffWindowsCopilot'; onVal=1; offVal=0 },
        @{ id='widgets'; name='Disable Widgets / News'; desc='Turns off Taskbar Widgets board'; reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; prop='TaskbarDa'; onVal=0; offVal=1 },
        @{ id='start_ads'; name='Disable Start Suggestions'; desc='Turns off suggested apps & tips in Start'; reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; prop='SubscribedContent-338388Enabled'; onVal=0; offVal=1 },
        @{ id='lock_ads'; name='Disable Lock Screen Tips'; desc='Turns off Windows Spotlight fun facts and tips'; reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; prop='RotatingLockScreenOverlayEnabled'; onVal=0; offVal=1 },
        @{ id='fast_startup'; name='Show File Extensions'; desc='Always display known file extensions in Explorer'; reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; prop='HideFileExt'; onVal=0; offVal=1 }
    )
    $out = @()
    foreach ($t in $tweaks) {
        $active = $false
        try {
            if (Test-Path $t.reg) {
                $v = (Get-ItemProperty -Path $t.reg -Name $t.prop -ErrorAction SilentlyContinue).($t.prop)
                if ($null -ne $v -and [int]$v -eq $t.onVal) { $active = $true }
            }
        } catch { }
        $out += [pscustomobject]@{ id = $t.id; name = $t.name; desc = $t.desc; active = $active }
    }
    return [pscustomobject]@{ ok = $true; items = $out }
}

function Apply-DebloatTweak([string]$id, [bool]$enable) {
    $map = @{
        bing_search  = @{ reg='HKCU:\Software\Policies\Microsoft\Windows\Explorer'; prop='DisableSearchBoxSuggestions'; on=1; off=0 }
        copilot      = @{ reg='HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot'; prop='TurnOffWindowsCopilot'; on=1; off=0 }
        widgets      = @{ reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; prop='TaskbarDa'; on=0; off=1 }
        start_ads    = @{ reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; prop='SubscribedContent-338388Enabled'; on=0; off=1 }
        lock_ads     = @{ reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; prop='RotatingLockScreenOverlayEnabled'; on=0; off=1 }
        fast_startup = @{ reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; prop='HideFileExt'; on=0; off=1 }
    }
    if (-not $map.ContainsKey($id)) { return [pscustomobject]@{ ok = $false; error = 'Unknown tweak' } }
    $t = $map[$id]
    if (-not (Test-Path $t.reg)) { New-Item -Path $t.reg -Force | Out-Null }
    $val = if ($enable) { $t.on } else { $t.off }
    Set-ItemProperty -Path $t.reg -Name $t.prop -Value $val -Type DWord -ErrorAction Stop
    return [pscustomobject]@{ ok = $true; active = $enable }
}

function Run-SystemRepair([string]$kind) {
    $cmd = switch ($kind) {
        'sfc'   { 'sfc /scannow' }
        'dism'  { 'DISM /Online /Cleanup-Image /RestoreHealth' }
        'net'   { 'ipconfig /flushdns & netsh winsock reset & netsh int ip reset' }
        'wu'    { 'net stop wuauserv & net stop bits & net start wuauserv & net start bits' }
        default { '' }
    }
    if (-not $cmd) { return [pscustomobject]@{ ok = $false; error = 'Unknown repair' } }
    if (Get-Command Write-JobLog -ErrorAction SilentlyContinue) { Write-JobLog "Running: $cmd" }
    $out = & cmd.exe /c $cmd 2>&1
    if (Get-Command Write-JobLog -ErrorAction SilentlyContinue) {
        foreach ($l in $out) {
            $s = ([string]$l -replace "`0", '').Trim()
            if ($s) { Write-JobLog $s }
        }
        Write-JobLog "Completed (exit code $LASTEXITCODE)"
    }
    return [pscustomobject]@{ ok = ($LASTEXITCODE -eq 0); exitCode = $LASTEXITCODE }
}
