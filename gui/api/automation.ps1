function Invoke-automationAction([string]$action, $argsObj) {
    switch ($action) {
        'tasks_list' { return Get-MaintenanceTasks }
        'task_create' { return Create-MaintenanceTask $argsObj }
        'task_delete' { return Delete-MaintenanceTask ([string]$argsObj.name) }
        'task_run' {
            schtasks.exe /Run /TN ("WinHealthAudit\" + [string]$argsObj.name) 2>&1 | Out-Null
            return [pscustomobject]@{ ok = ($LASTEXITCODE -eq 0) }
        }
        'backups_list' { return Get-AllBackups }
        default { return [pscustomobject]@{ ok = $false; error = "Unknown action: $action" } }
    }
}

function Get-MaintenanceTasks {
    $tasks = @()
    try {
        Get-ScheduledTask -TaskPath '\WinHealthAudit\' -ErrorAction SilentlyContinue | ForEach-Object {
            $info = $_ | Get-ScheduledTaskInfo -ErrorAction SilentlyContinue
            $tasks += [pscustomobject]@{
                name = $_.TaskName
                state = [string]$_.State
                lastRun = if ($info -and $info.LastRunTime -and $info.LastRunTime.Year -gt 1999) { $info.LastRunTime.ToString('yyyy-MM-dd HH:mm') } else { 'Never' }
                nextRun = if ($info -and $info.NextRunTime -and $info.NextRunTime.Year -gt 1999) { $info.NextRunTime.ToString('yyyy-MM-dd HH:mm') } else { '-' }
            }
        }
    } catch { }
    return [pscustomobject]@{ ok = $true; items = $tasks }
}

function Create-MaintenanceTask($argsObj) {
    $name = ($argsObj.name -replace '[^a-zA-Z0-9_-]', '')
    if (-not $name) { $name = 'ScheduledAudit' }
    $sched = switch ([string]$argsObj.schedule) {
        'daily'  { 'DAILY' }
        'weekly' { 'WEEKLY' }
        'logon'  { 'ONLOGON' }
        default  { 'DAILY' }
    }
    $time = if ($argsObj.time -match '^\d{2}:\d{2}$') { $argsObj.time } else { '09:00' }
    $kind = [string]$argsObj.kind
    $cmd = switch ($kind) {
        'clean' { 'powershell.exe -NoProfile -WindowStyle Hidden -Command "Remove-Item $env:TEMP\* -Recurse -Force -ErrorAction SilentlyContinue"' }
        default {
            $exe = Join-Path (Split-Path -Parent $script:WhaSettingsDir) 'WinHealthAudit.Core.exe'
            if (-not (Test-Path $exe)) { $exe = Join-Path $script:WhaSettingsDir 'WinHealthAudit.Core.exe' }
            "`"$exe`" --quiet"
        }
    }
    $tn = "WinHealthAudit\$name"
    if ($sched -eq 'ONLOGON') {
        & schtasks.exe /Create /TN $tn /TR $cmd /SC ONLOGON /F 2>&1 | Out-Null
    } else {
        & schtasks.exe /Create /TN $tn /TR $cmd /SC $sched /ST $time /F 2>&1 | Out-Null
    }
    return [pscustomobject]@{ ok = ($LASTEXITCODE -eq 0); exitCode = $LASTEXITCODE }
}

function Delete-MaintenanceTask([string]$name) {
    $tn = "WinHealthAudit\$name"
    & schtasks.exe /Delete /TN $tn /F 2>&1 | Out-Null
    return [pscustomobject]@{ ok = ($LASTEXITCODE -eq 0) }
}

function Get-AllBackups {
    $restorePoints = @()
    try {
        Get-ComputerRestorePoint -ErrorAction SilentlyContinue | Sort-Object SequenceNumber -Descending | Select-Object -First 20 | ForEach-Object {
            $dt = ''
            try { $dt = [Management.ManagementDateTimeConverter]::ToDateTime($_.CreationTime).ToString('yyyy-MM-dd HH:mm') } catch { }
            $restorePoints += [pscustomobject]@{
                seq = $_.SequenceNumber
                desc = $_.Description
                date = $dt
            }
        }
    } catch { }

    $driverBackups = @()
    $dbDir = Join-Path $script:WhaSettingsDir 'driver-backups'
    if (Test-Path -LiteralPath $dbDir) {
        Get-ChildItem -Path $dbDir -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $driverBackups += [pscustomobject]@{ name = $_.Name; path = $_.FullName; date = $_.LastWriteTime.ToString('yyyy-MM-dd HH:mm') }
        }
    }

    $quarantine = @()
    $qDir = Join-Path $script:WhaSettingsDir 'quarantine'
    if (Test-Path -LiteralPath $qDir) {
        Get-ChildItem -Path $qDir -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $quarantine += [pscustomobject]@{ id = $_.Name; path = $_.FullName; date = $_.LastWriteTime.ToString('yyyy-MM-dd HH:mm') }
        }
    }

    return [pscustomobject]@{
        ok = $true
        restorePoints = $restorePoints
        driverBackups = $driverBackups
        quarantine = $quarantine
    }
}
