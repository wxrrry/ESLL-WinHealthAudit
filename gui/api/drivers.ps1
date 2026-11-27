function Invoke-driversAction([string]$action, $argsObj) {
    switch ($action) {
        'list' { return Get-DriverInventory }
        'problems' { return Get-ProblemDevices }
        'wu_scan' { return Scan-WindowsUpdateDrivers }
        'export' { return Export-AllDrivers ([string]$argsObj.dest) }
        'backups' { return Get-DriverBackups }
        'toggle_device' {
            $id = [string]$argsObj.deviceId
            $enable = [bool]$argsObj.enable
            if (-not $id) { return [pscustomobject]@{ ok = $false; error = 'Missing deviceId' } }
            if ($enable) {
                Enable-PnpDevice -InstanceId $id -Confirm:$false -ErrorAction Stop
            } else {
                Disable-PnpDevice -InstanceId $id -Confirm:$false -ErrorAction Stop
            }
            return [pscustomobject]@{ ok = $true }
        }
        'restore_point' {
            $desc = if ($argsObj.desc) { [string]$argsObj.desc } else { 'WinHealthAudit Driver Checkpoint' }
            Checkpoint-Computer -Description $desc -RestorePointType 'DEVICE_DRIVER_INSTALL' -ErrorAction Stop
            return [pscustomobject]@{ ok = $true }
        }
        default { return [pscustomobject]@{ ok = $false; error = "Unknown action: $action" } }
    }
}

function Get-DriverInventory {
    $items = @(Get-CimInstance Win32_PnPSignedDriver -ErrorAction SilentlyContinue |
        Where-Object { $_.DeviceName } |
        Sort-Object DeviceClass, DeviceName |
        ForEach-Object {
            $dt = ''
            if ($_.DriverDate) {
                try { $dt = ([DateTime]$_.DriverDate).ToString('yyyy-MM-dd') } catch { }
            }
            [pscustomobject]@{
                name = $_.DeviceName
                class = $_.DeviceClass
                provider = $_.DriverProviderName
                version = $_.DriverVersion
                date = $dt
                signed = [bool]$_.IsSigned
                deviceId = $_.DeviceID
            }
        })
    return [pscustomobject]@{ ok = $true; count = $items.Count; items = $items }
}

function Get-ProblemDevices {
    $problems = @(Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue |
        Where-Object { $_.ConfigManagerErrorCode -ne 0 } |
        ForEach-Object {
            [pscustomobject]@{
                name = $_.Name
                class = $_.PNPClass
                status = $_.Status
                errorCode = [int]$_.ConfigManagerErrorCode
                deviceId = $_.PNPDeviceID
            }
        })
    return [pscustomobject]@{ ok = $true; count = $problems.Count; items = $problems }
}

function Scan-WindowsUpdateDrivers {
    if (Get-Command Write-JobLog -ErrorAction SilentlyContinue) { Write-JobLog 'Connecting to Windows Update Agent...' }
    $session = New-Object -ComObject Microsoft.Update.Session
    $searcher = $session.CreateUpdateSearcher()
    if (Get-Command Write-JobLog -ErrorAction SilentlyContinue) { Write-JobLog 'Searching for driver updates (Microsoft signed)...' }
    $res = $searcher.Search("IsInstalled=0 and Type='Driver'")
    $updates = @()
    foreach ($u in $res.Updates) {
        $updates += [pscustomobject]@{
            title = $u.Title
            driverModel = $u.DriverModel
            driverVerDate = if ($u.DriverVerDate) { $u.DriverVerDate.ToString('yyyy-MM-dd') } else { '' }
            manufacturer = $u.DriverManufacturer
            sizeMB = [math]::Round($u.MaxDownloadSize / 1MB, 1)
            reboot = [bool]$u.RebootRequired
        }
    }
    if (Get-Command Write-JobLog -ErrorAction SilentlyContinue) { Write-JobLog ("Found " + $updates.Count + " driver update(s).") }
    return [pscustomobject]@{ ok = $true; count = $updates.Count; items = $updates }
}

function Get-DriverBackups {
    $base = Join-Path $script:WhaSettingsDir 'driver-backups'
    if (-not (Test-Path -LiteralPath $base)) {
        return [pscustomobject]@{ ok = $true; items = @() }
    }
    $dirs = @(Get-ChildItem -Path $base -Directory -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | ForEach-Object {
        $files = @(Get-ChildItem -Path $_.FullName -Filter '*.inf' -Recurse -ErrorAction SilentlyContinue).Count
        [pscustomobject]@{
            name = $_.Name
            path = $_.FullName
            date = $_.LastWriteTime.ToString('yyyy-MM-dd HH:mm')
            infCount = $files
        }
    })
    return [pscustomobject]@{ ok = $true; items = $dirs }
}

function Export-AllDrivers([string]$dest) {
    if (-not $dest) {
        $base = Join-Path $script:WhaSettingsDir 'driver-backups'
        $dest = Join-Path $base ('backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    }
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    if (Get-Command Write-JobLog -ErrorAction SilentlyContinue) { Write-JobLog ("Exporting third-party drivers to " + $dest) }
    $out = & pnputil.exe /export-driver * "$dest" 2>&1
    if (Get-Command Write-JobLog -ErrorAction SilentlyContinue) {
        foreach ($line in $out) { if ($line) { Write-JobLog ([string]$line) } }
    }
    return [pscustomobject]@{ ok = ($LASTEXITCODE -eq 0); path = $dest; exitCode = $LASTEXITCODE }
}
