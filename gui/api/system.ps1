function Invoke-systemAction([string]$action, $argsObj) {
    switch ($action) {
        'overview' { return Get-SystemOverview }
        'overview_counts' { return Get-OverviewCounts }
        'telemetry' { return Get-LiveTelemetry }
        'processes' { return Get-TopProcesses $argsObj }
        'end_process' {
            $pidVal = [int]$argsObj.pid
            if ($pidVal -le 4) { return [pscustomobject]@{ ok = $false; error = 'Protected system PID' } }
            Stop-Process -Id $pidVal -Force -ErrorAction Stop
            return [pscustomobject]@{ ok = $true }
        }
        'open_location' {
            $p = [string]$argsObj.path
            if ($p -and (Test-Path -LiteralPath $p)) {
                Start-Process 'explorer.exe' -ArgumentList ('/select,"' + $p + '"')
                return [pscustomobject]@{ ok = $true }
            }
            return [pscustomobject]@{ ok = $false; error = 'Path not found' }
        }
        'dns_test' { return Test-DnsServers }
        'dns_set' { return Set-DnsPreset ([string]$argsObj.preset) }
        default { return [pscustomobject]@{ ok = $false; error = "Unknown action: $action" } }
    }
}

function Get-OverviewCounts {
    $progCount = 0
    try {
        $paths = @(
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
        )
        $progCount = @(Get-ItemProperty $paths -Name DisplayName -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName }).Count
    } catch { }
    $drvCount = 0
    try {
        $drvCount = @(Get-CimInstance Win32_PnPSignedDriver -Property DeviceName -ErrorAction SilentlyContinue | Where-Object { $_.DeviceName }).Count
    } catch { }
    return [pscustomobject]@{ ok = $true; programs = $progCount; drivers = $drvCount }
}

function Get-SystemOverview {
    $cs = Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue | Select-Object -First 1
    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue | Select-Object -First 1
    $cpu = Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1
    $board = Get-CimInstance Win32_BaseBoard -ErrorAction SilentlyContinue | Select-Object -First 1
    $bios = Get-CimInstance Win32_BIOS -ErrorAction SilentlyContinue | Select-Object -First 1
    $gpus = @(Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | ForEach-Object {
        [pscustomobject]@{
            name = $_.Name
            driver = $_.DriverVersion
            vramMB = if ($_.AdapterRAM) { [math]::Round($_.AdapterRAM / 1MB) } else { 0 }
            res = if ($_.CurrentHorizontalResolution) { "$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution) @ $($_.CurrentRefreshRate)Hz" } else { '' }
        }
    })

    $ramTotalGB = if ($cs.TotalPhysicalMemory) { [math]::Round($cs.TotalPhysicalMemory / 1GB, 1) } else { 0 }
    $ramFreeGB = if ($os.FreePhysicalMemory) { [math]::Round(($os.FreePhysicalMemory * 1KB) / 1GB, 1) } else { 0 }
    $uptimeHours = 0
    if ($os.LastBootUpTime) {
        $uptimeHours = [math]::Round(((Get-Date) - $os.LastBootUpTime).TotalHours, 1)
    }

    $chassis = Get-CimInstance Win32_SystemEnclosure -ErrorAction SilentlyContinue | Select-Object -First 1
    $isLaptop = $false
    if ($chassis -and $chassis.ChassisTypes) {
        foreach ($t in $chassis.ChassisTypes) {
            if ($t -in @(8,9,10,11,12,14,18,21,30,31,32)) { $isLaptop = $true }
        }
    }

    $displays = @()
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
        foreach ($scr in [System.Windows.Forms.Screen]::AllScreens) {
            $displays += [pscustomobject]@{
                name = $scr.DeviceName
                primary = $scr.Primary
                width = $scr.Bounds.Width
                height = $scr.Bounds.Height
            }
        }
    } catch { }

    return [pscustomobject]@{
        ok = $true
        hostname = $env:COMPUTERNAME
        user = $env:USERNAME
        manufacturer = $cs.Manufacturer
        model = $cs.Model
        isLaptop = $isLaptop
        os = "$($os.Caption) ($($os.Version), Build $($os.BuildNumber))"
        uptimeHours = $uptimeHours
        cpu = [pscustomobject]@{
            name = ($cpu.Name -replace '\s+', ' ').Trim()
            cores = $cpu.NumberOfCores
            threads = $cpu.NumberOfLogicalProcessors
            maxMhz = $cpu.MaxClockSpeed
            load = [int]$cpu.LoadPercentage
        }
        ram = [pscustomobject]@{
            totalGB = $ramTotalGB
            usedGB = [math]::Round($ramTotalGB - $ramFreeGB, 1)
            freeGB = $ramFreeGB
            pct = if ($ramTotalGB -gt 0) { [math]::Round((($ramTotalGB - $ramFreeGB) / $ramTotalGB) * 100) } else { 0 }
        }
        board = "$($board.Manufacturer) $($board.Product)"
        bios = "$($bios.Manufacturer) $($bios.SMBIOSBIOSVersion)"
        gpus = $gpus
        displays = $displays
    }
}

function Get-LiveTelemetry {
    $cpuLoad = 0
    $cpuClock = 0
    try {
        $c = Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1
        $cpuLoad = [int]$c.LoadPercentage
        $cpuClock = [int]$c.CurrentClockSpeed
    } catch { }

    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue | Select-Object -First 1
    $totalMB = if ($os.TotalVisibleMemorySize) { [math]::Round($os.TotalVisibleMemorySize / 1024) } else { 0 }
    $freeMB = if ($os.FreePhysicalMemory) { [math]::Round($os.FreePhysicalMemory / 1024) } else { 0 }
    $usedMB = $totalMB - $freeMB
    $ramPct = if ($totalMB -gt 0) { [math]::Round(($usedMB / $totalMB) * 100) } else { 0 }

    $cpuTempC = $null
    try {
        $tz = Get-CimInstance -Namespace root/wmi -ClassName MSAcpi_ThermalZoneTemperature -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($tz -and $tz.CurrentTemperature) {
            $cpuTempC = [math]::Round(($tz.CurrentTemperature / 10.0) - 273.15, 1)
        }
    } catch { }

    $gpuInfo = $null
    try {
        $nv = Get-Command 'nvidia-smi.exe' -ErrorAction SilentlyContinue
        if ($nv) {
            $out = & $nv.Source --query-gpu=name,utilization.gpu,temperature.gpu,memory.used,memory.total,power.draw --format=csv,noheader,nounits 2>$null | Select-Object -First 1
            if ($out) {
                $p = $out -split ','
                if ($p.Count -ge 6) {
                    $gpuInfo = [pscustomobject]@{
                        name = $p[0].Trim()
                        load = [int]$p[1].Trim()
                        tempC = [int]$p[2].Trim()
                        vramUsedMB = [int]$p[3].Trim()
                        vramTotalMB = [int]$p[4].Trim()
                        powerW = $p[5].Trim()
                    }
                }
            }
        }
    } catch { }

    if (-not $gpuInfo) {
        $vc = Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($vc) {
            $gpuInfo = [pscustomobject]@{
                name = $vc.Name
                load = $null
                tempC = $null
                vramUsedMB = $null
                vramTotalMB = if ($vc.AdapterRAM) { [math]::Round($vc.AdapterRAM / 1MB) } else { $null }
                powerW = $null
            }
        }
    }

    $disks = @()
    try {
        $vols = Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" -ErrorAction SilentlyContinue
        foreach ($v in $vols) {
            $tot = if ($v.Size) { [math]::Round($v.Size / 1GB, 1) } else { 0 }
            $fre = if ($v.FreeSpace) { [math]::Round($v.FreeSpace / 1GB, 1) } else { 0 }
            $disks += [pscustomobject]@{
                drive = $v.DeviceID
                label = $v.VolumeName
                fs = $v.FileSystem
                totalGB = $tot
                freeGB = $fre
                usedPct = if ($tot -gt 0) { [math]::Round((($tot - $fre) / $tot) * 100) } else { 0 }
            }
        }
    } catch { }

    $physDisks = @()
    try {
        foreach ($pd in @(Get-PhysicalDisk -ErrorAction SilentlyContinue)) {
            $rel = $null
            try { $rel = $pd | Get-StorageReliabilityCounter -ErrorAction SilentlyContinue } catch { }
            $physDisks += [pscustomobject]@{
                name = $pd.FriendlyName
                media = [string]$pd.MediaType
                health = [string]$pd.HealthStatus
                sizeGB = [math]::Round($pd.Size / 1GB)
                tempC = if ($rel -and $rel.Temperature) { $rel.Temperature } else { $null }
                wear = if ($rel -and $null -ne $rel.Wear) { $rel.Wear } else { $null }
            }
        }
    } catch { }

    $net = @()
    try {
        foreach ($ad in @(Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' })) {
            $ip = (Get-NetIPAddress -InterfaceIndex $ad.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object -First 1).IPAddress
            $net += [pscustomobject]@{
                name = $ad.Name
                desc = $ad.InterfaceDescription
                speed = $ad.LinkSpeed
                ip = $ip
                mac = $ad.MacAddress
            }
        }
    } catch { }

    $wifi = $null
    try {
        $w = & netsh wlan show interfaces 2>$null
        if ($w) {
            $ssid = ($w | Select-String '^\s*SSID\s*:\s*(.+)$' | ForEach-Object { $_.Matches[0].Groups[1].Value.Trim() } | Select-Object -First 1)
            $sig  = ($w | Select-String '^\s*Signal\s*:\s*(.+)$' | ForEach-Object { $_.Matches[0].Groups[1].Value.Trim() } | Select-Object -First 1)
            if ($ssid) { $wifi = [pscustomobject]@{ ssid = $ssid; signal = $sig } }
        }
    } catch { }

    $battery = $null
    try {
        $bat = Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($bat) {
            $battery = [pscustomobject]@{
                pct = [int]$bat.EstimatedChargeRemaining
                status = switch ([int]$bat.BatteryStatus) { 1 {'Discharging'} 2 {'AC'} 3 {'Full'} default {'Charging'} }
                runtimeMin = if ($bat.EstimatedRunTime -and $bat.EstimatedRunTime -lt 71582788) { [int]$bat.EstimatedRunTime } else { $null }
            }
        }
    } catch { }

    return [pscustomobject]@{
        ok = $true
        ts = (Get-Date).ToString('HH:mm:ss')
        cpuLoad = $cpuLoad
        cpuClock = $cpuClock
        cpuTempC = $cpuTempC
        ram = [pscustomobject]@{ totalMB = $totalMB; usedMB = $usedMB; freeMB = $freeMB; pct = $ramPct }
        gpu = $gpuInfo
        disks = $disks
        physDisks = $physDisks
        net = $net
        wifi = $wifi
        battery = $battery
    }
}

function Get-TopProcesses($argsObj) {
    $sortBy = if ($argsObj -and $argsObj.sort -eq 'cpu') { 'CPU' } else { 'WorkingSet64' }
    $procs = Get-Process -ErrorAction SilentlyContinue |
        Sort-Object -Property $sortBy -Descending |
        Select-Object -First 60 |
        ForEach-Object {
            $path = ''
            try { $path = $_.Path } catch { }
            [pscustomobject]@{
                pid = $_.Id
                name = $_.ProcessName
                ramMB = [math]::Round($_.WorkingSet64 / 1MB, 1)
                cpu = if ($_.CPU) { [math]::Round($_.CPU, 1) } else { 0 }
                threads = $_.Threads.Count
                path = $path
            }
        }
    return [pscustomobject]@{ ok = $true; items = @($procs) }
}

function Test-DnsServers {
    $targets = @(
        @{ name = 'Cloudflare'; ip = '1.1.1.1' },
        @{ name = 'Google'; ip = '8.8.8.8' },
        @{ name = 'Quad9'; ip = '9.9.9.9' },
        @{ name = 'AdGuard'; ip = '94.140.14.14' }
    )
    $res = @()
    foreach ($t in $targets) {
        $ms = $null
        try {
            $ping = New-Object System.Net.NetworkInformation.Ping
            $reply = $ping.Send($t.ip, 1200)
            if ($reply.Status -eq 'Success') { $ms = [int]$reply.RoundtripTime }
        } catch { }
        $res += [pscustomobject]@{ name = $t.name; ip = $t.ip; ms = $ms }
    }
    return [pscustomobject]@{ ok = $true; results = $res }
}

function Set-DnsPreset([string]$preset) {
    $servers = switch ($preset) {
        'cloudflare' { @('1.1.1.1', '1.0.0.1') }
        'google'     { @('8.8.8.8', '8.8.4.4') }
        'quad9'      { @('9.9.9.9', '149.112.112.112') }
        'auto'       { @() }
        default      { return [pscustomobject]@{ ok = $false; error = 'Invalid DNS preset' } }
    }
    $adapters = @(Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' })
    foreach ($ad in $adapters) {
        if ($servers.Count -eq 0) {
            Set-DnsClientServerAddress -InterfaceIndex $ad.ifIndex -ResetServerAddresses -ErrorAction Stop
        } else {
            Set-DnsClientServerAddress -InterfaceIndex $ad.ifIndex -ServerAddresses $servers -ErrorAction Stop
        }
    }
    return [pscustomobject]@{ ok = $true; preset = $preset }
}
