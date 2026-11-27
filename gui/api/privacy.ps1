function Invoke-privacyAction([string]$action, $argsObj) {
    switch ($action) {
        'overview' { return Get-PrivacyOverview }
        'ports' { return Get-OpenPorts }
        'permissions' { return Get-CapabilityPermissions }
        'tracking_list' { return Get-TrackingToggles }
        'tracking_set' { return Set-TrackingToggle ([string]$argsObj.id) ([bool]$argsObj.protected) }
        'persistence' { return Get-PersistenceAudit }
        default { return [pscustomobject]@{ ok = $false; error = "Unknown action: $action" } }
    }
}

function Get-PrivacyOverview {
    $score = 100
    $findings = @()

    $defEnabled = $false
    try {
        $mp = Get-MpComputerStatus -ErrorAction SilentlyContinue
        if ($mp -and $mp.RealTimeProtectionEnabled) { $defEnabled = $true }
    } catch { }
    if (-not $defEnabled) { $score -= 20; $findings += [pscustomobject]@{ sev='warning'; title='Windows Defender Real-Time Protection'; detail='Not active or managed by third-party AV' } }
    else { $findings += [pscustomobject]@{ sev='ok'; title='Windows Defender Real-Time Protection'; detail='Active and monitoring' } }

    $fwOk = $true
    try {
        $profiles = @(Get-NetFirewallProfile -ErrorAction SilentlyContinue)
        foreach ($p in $profiles) { if (-not $p.Enabled) { $fwOk = $false } }
    } catch { }
    if (-not $fwOk) { $score -= 15; $findings += [pscustomobject]@{ sev='warning'; title='Windows Firewall'; detail='One or more network profiles have firewall disabled' } }
    else { $findings += [pscustomobject]@{ sev='ok'; title='Windows Firewall'; detail='All profiles enabled' } }

    $uacOk = $true
    try {
        $uac = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -ErrorAction SilentlyContinue).EnableLUA
        if ($uac -eq 0) { $uacOk = $false }
    } catch { }
    if (-not $uacOk) { $score -= 20; $findings += [pscustomobject]@{ sev='critical'; title='User Account Control (UAC)'; detail='UAC is disabled' } }
    else { $findings += [pscustomobject]@{ sev='ok'; title='User Account Control (UAC)'; detail='Enabled' } }

    $sb = $null
    try { $sb = Confirm-SecureBootUEFI -ErrorAction SilentlyContinue } catch { }
    if ($sb -eq $true) { $findings += [pscustomobject]@{ sev='ok'; title='Secure Boot'; detail='Enabled' } }
    elseif ($sb -eq $false) { $score -= 10; $findings += [pscustomobject]@{ sev='warning'; title='Secure Boot'; detail='Disabled in UEFI' } }

    $tpmOk = $false
    try {
        $tpm = Get-Tpm -ErrorAction SilentlyContinue
        if ($tpm -and $tpm.TpmPresent -and $tpm.TpmReady) { $tpmOk = $true }
    } catch { }
    if ($tpmOk) { $findings += [pscustomobject]@{ sev='ok'; title='TPM 2.0'; detail='Present and ready' } }
    else { $score -= 5; $findings += [pscustomobject]@{ sev='info'; title='TPM'; detail='Not detected or requires administrator privileges' } }

    return [pscustomobject]@{ ok = $true; score = [math]::Max(0, $score); findings = $findings }
}

function Get-OpenPorts {
    $procs = @{}
    Get-Process -ErrorAction SilentlyContinue | ForEach-Object { $procs[[int]$_.Id] = $_.ProcessName }
    $ports = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Sort-Object LocalPort | Select-Object -First 80 | ForEach-Object {
        $pname = if ($procs.ContainsKey([int]$_.OwningProcess)) { $procs[[int]$_.OwningProcess] } else { 'System' }
        [pscustomobject]@{
            port = [int]$_.LocalPort
            address = $_.LocalAddress
            pid = [int]$_.OwningProcess
            process = $pname
            scope = if ($_.LocalAddress -in @('127.0.0.1','::1')) { 'Localhost' } else { 'Network' }
        }
    })
    return [pscustomobject]@{ ok = $true; items = $ports }
}

function Get-CapabilityPermissions {
    $caps = @('webcam', 'microphone', 'location')
    $res = @()
    foreach ($cap in $caps) {
        $base = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\$cap"
        if (-not (Test-Path $base)) { continue }
        Get-ChildItem -Path $base -ErrorAction SilentlyContinue | ForEach-Object {
            $props = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
            $last = $null
            if ($props.LastUsedTimeStop -and $props.LastUsedTimeStop -gt 0) {
                try { $last = [DateTime]::FromFileTime([int64]$props.LastUsedTimeStop).ToString('yyyy-MM-dd HH:mm') } catch { }
            }
            $name = $_.PSChildName -replace '#', '\'
            $res += [pscustomobject]@{
                capability = $cap
                app = $name
                value = [string]$props.Value
                lastUsed = $last
            }
        }
    }
    return [pscustomobject]@{ ok = $true; items = @($res | Sort-Object lastUsed -Descending | Select-Object -First 60) }
}

function Get-TrackingToggles {
    $defs = @(
        @{ id='ad_id'; name='Advertising ID'; desc='Prevent apps from using advertising ID for cross-app tracking'; reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo'; prop='Enabled'; protVal=0; openVal=1 },
        @{ id='tailored'; name='Tailored Experiences'; desc='Prevent Microsoft from using diagnostic data for personalized tips'; reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy'; prop='TailoredExperiencesWithDiagnosticDataEnabled'; protVal=0; openVal=1 },
        @{ id='app_launch'; name='App Launch Tracking'; desc='Stop Windows from tracking app launches to improve Start'; reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; prop='Start_TrackProgs'; protVal=0; openVal=1 },
        @{ id='typing_info'; name='Inking & Typing Personalization'; desc='Prevent Windows from collecting typing/inking dictionary'; reg='HKCU:\Software\Microsoft\InputPersonalization'; prop='RestrictImplicitTextCollection'; protVal=1; openVal=0 },
        @{ id='feedback'; name='Feedback Frequency'; desc='Set Windows feedback frequency to Never'; reg='HKCU:\Software\Microsoft\Siuf\Rules'; prop='NumberOfSIUFInPeriod'; protVal=0; openVal=1 },
        @{ id='activity'; name='Activity History'; desc='Disable publishing user activities to timeline/cloud'; reg='HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'; prop='PublishUserActivities'; protVal=0; openVal=1 }
    )
    $items = @()
    foreach ($d in $defs) {
        $prot = $false
        try {
            if (Test-Path $d.reg) {
                $v = (Get-ItemProperty -Path $d.reg -Name $d.prop -ErrorAction SilentlyContinue).($d.prop)
                if ($null -ne $v -and [int]$v -eq $d.protVal) { $prot = $true }
            }
        } catch { }
        $items += [pscustomobject]@{ id = $d.id; name = $d.name; desc = $d.desc; protected = $prot }
    }
    return [pscustomobject]@{ ok = $true; items = $items }
}

function Set-TrackingToggle([string]$id, [bool]$protected) {
    $map = @{
        ad_id       = @{ reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo'; prop='Enabled'; prot=0; open=1 }
        tailored    = @{ reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy'; prop='TailoredExperiencesWithDiagnosticDataEnabled'; prot=0; open=1 }
        app_launch  = @{ reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; prop='Start_TrackProgs'; prot=0; open=1 }
        typing_info = @{ reg='HKCU:\Software\Microsoft\InputPersonalization'; prop='RestrictImplicitTextCollection'; prot=1; open=0 }
        feedback    = @{ reg='HKCU:\Software\Microsoft\Siuf\Rules'; prop='NumberOfSIUFInPeriod'; prot=0; open=1 }
        activity    = @{ reg='HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'; prop='PublishUserActivities'; prot=0; open=1 }
    }
    if (-not $map.ContainsKey($id)) { return [pscustomobject]@{ ok = $false; error = 'Unknown toggle' } }
    $t = $map[$id]
    if (-not (Test-Path $t.reg)) { New-Item -Path $t.reg -Force | Out-Null }
    $val = if ($protected) { $t.prot } else { $t.open }
    Set-ItemProperty -Path $t.reg -Name $t.prop -Value $val -Type DWord -ErrorAction Stop
    return [pscustomobject]@{ ok = $true; protected = $protected }
}

function Get-PersistenceAudit {
    $items = @()
    try {
        Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.State -ne 'Disabled' -and $_.TaskPath -notlike '\Microsoft\*' } | Select-Object -First 40 | ForEach-Object {
            $items += [pscustomobject]@{
                kind = 'ScheduledTask'
                name = $_.TaskName
                path = $_.TaskPath
                state = [string]$_.State
            }
        }
    } catch { }
    return [pscustomobject]@{ ok = $true; items = $items }
}
