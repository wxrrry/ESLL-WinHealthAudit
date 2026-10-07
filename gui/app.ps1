# WinHealthAudit window - local launcher.
# Runs the engine in a PowerShell job, keeps progress, result and settings in
# memory and serves the page over a loopback-only HTTP server. The window is
# an Edge --app window, so it behaves like a desktop application.
#
# Started by WinHealthAudit.exe (which passes -Engine and -Launcher)
# or directly for development:
#
#   .\app.ps1              start, open the window
#   .\app.ps1 -NoBrowser   server only (for testing)
#   ctrl+c                 stop

param(
    [int]$Days = 14,
    [int]$Port = 8777,
    [string]$Engine = '',
    [string]$Launcher = '',
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'

$guiDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $guiDir
if (-not $Engine) {
    $Engine = Join-Path $rootDir 'bin\WinHealthAudit.Core.exe'
    if (-not (Test-Path $Engine)) {
        $side = Join-Path $rootDir 'WinHealthAudit.Core.exe'
        if (Test-Path $side) { $Engine = $side }
    }
}
$reports = Join-Path $rootDir 'reports'
$page    = Join-Path $guiDir 'index.html'
$html    = [System.IO.File]::ReadAllBytes($page)

$settingsDir  = Join-Path $env:LOCALAPPDATA 'WinHealthAudit'
$settingsPath = Join-Path $settingsDir 'settings.json'
$lastPath     = Join-Path $settingsDir 'last-result.json'

# --- settings ------------------------------------------------------------

function Merge-Settings($loaded) {
    $s = [pscustomobject]@{
        days             = 14
        autoRun          = $false
        lang             = 'en'
        out              = ''
        startWithWindows = $false
        hidden           = @()
    }

    if ($loaded) {
        foreach ($name in @($s.PSObject.Properties.Name)) {
            if ($loaded.PSObject.Properties[$name]) { $s.$name = $loaded.$name }
        }
    }

    $s.days = [Math]::Min(365, [Math]::Max(1, [int]$s.days))
    if ($s.lang -ne 'ru') { $s.lang = 'en' }
    return $s
}

$loaded = $null
if (Test-Path $settingsPath) {
    try { $loaded = Get-Content $settingsPath -Raw | ConvertFrom-Json } catch { }
}
$script:settings = Merge-Settings $loaded

function Save-Settings {
    New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
    $script:settings | ConvertTo-Json -Depth 6 | Set-Content $settingsPath -Encoding UTF8
}

function Apply-Startup($enabled) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

    if ($enabled) {
        $command = if ($Launcher) {
            '"' + $Launcher + '"'
        } else {
            'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'
        }
        Set-ItemProperty -Path $runKey -Name 'WinHealthAudit' -Value $command
    } else {
        Remove-ItemProperty -Path $runKey -Name 'WinHealthAudit' -ErrorAction SilentlyContinue
    }
}

function Write-Settings($query) {
    foreach ($pair in ($query -split '&')) {
        if (-not $pair) { continue }
        $kv = $pair -split '=', 2
        $name  = [Uri]::UnescapeDataString($kv[0])
        $value = if ($kv.Count -gt 1) { [Uri]::UnescapeDataString($kv[1]) } else { '' }

        switch ($name) {
            'days' {
                $script:settings.days = [Math]::Min(365, [Math]::Max(1, [int]$value))
                $script:days = $script:settings.days
            }
            'autoRun'          { $script:settings.autoRun = ($value -eq '1' -or $value -eq 'true') }
            'lang'             { $script:settings.lang = if ($value -eq 'ru') { 'ru' } else { 'en' } }
            'out'              { $script:settings.out = $value }
            'startWithWindows' {
                $script:settings.startWithWindows = ($value -eq '1' -or $value -eq 'true')
                Apply-Startup $script:settings.startWithWindows
            }
        }
    }

    Save-Settings
}

function Reports-Dir {
    if ($script:settings.out) {
        return [Environment]::ExpandEnvironmentVariables([string]$script:settings.out)
    }
    return $reports
}

# --- engine run ----------------------------------------------------------

$script:job        = $null
$script:phase      = 'idle'
$script:days       = $script:settings.days
$script:result     = $null
$script:prevResult = $null
$script:errorText  = ''
$script:log        = @()

function Get-PrevResult {
    try {
        if (Test-Path $lastPath) {
            $raw = Get-Content $lastPath -Raw
            if ($raw) { return ($raw | ConvertFrom-Json) }
        }
    } catch { }
    return $null
}

function Save-LastResult($res) {
    try {
        New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
        $res | ConvertTo-Json -Depth 12 | Set-Content $lastPath -Encoding UTF8
    } catch { }
}

function Update-Run {
    if ($script:phase -ne 'running' -or -not $script:job) { return }

    # Everything the engine printed so far: banner, progress lines, then the JSON.
    $all = @(Receive-Job -Job $script:job -Keep -ErrorAction SilentlyContinue |
             ForEach-Object { "$_" })
    $jsonLine = $all | Where-Object { $_.Trim().StartsWith('{') } | Select-Object -Last 1

    # The JSON belongs to the result, not to the log view.
    $script:log = @($all | Where-Object { -not $_.Trim().StartsWith('{') })

    if ($script:job.State -ne 'Completed' -and $script:job.State -ne 'Failed') { return }

    if ($jsonLine) {
        try {
            $script:result    = $jsonLine | ConvertFrom-Json
            $script:prevResult = Get-PrevResult
            Save-LastResult $script:result
            $script:phase     = 'done'
            $script:errorText = ''
        } catch {
            $script:phase     = 'failed'
            $script:errorText = 'the engine returned unreadable JSON: ' + $_.Exception.Message
        }
    } else {
        $script:phase     = 'failed'
        $script:errorText = 'the engine produced no result'
    }

    Remove-Job -Job $script:job -Force -ErrorAction SilentlyContinue
    $script:job = $null
}

function Start-Run([int]$days) {
    Update-Run
    if ($script:phase -eq 'running') { return }

    if (-not (Test-Path $Engine)) {
        $script:phase     = 'failed'
        $script:errorText = "missing engine: $Engine"
        return
    }

    $script:days      = $days
    $script:result    = $null
    $script:errorText = ''
    $script:log       = @()

    $output = Reports-Dir
    New-Item -ItemType Directory -Path $output -Force | Out-Null

    $script:job = Start-Job -Name 'WinHealthAudit' -ScriptBlock {
        param($engine, $days, $outDir)
        # stderr lines arrive wrapped in ErrorRecord - unwrap them, otherwise
        # PowerShell prints the exception type name instead of the line.
        & $engine --json --days $days --out $outDir 2>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.Exception.Message }
            else { "$_" }
        }
    } -ArgumentList $Engine, $days, $output
    $script:phase = 'running'
}

function Test-Elevated {
    $identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-State {
    Update-Run

    $elevated = Test-Elevated

    $done = 0; $total = 0; $current = ''
    foreach ($line in $script:log) {
        if ($line -match '^\[(\d+)/(\d+)\]\s+(.*)$') {
            $done    = [int]$Matches[1]
            $total   = [int]$Matches[2]
            $current = $Matches[3]
        }
    }

    [pscustomobject]@{
        phase    = $script:phase
        days     = $script:days
        done     = $done
        total    = $total
        current  = $current
        log      = @($script:log)
        result   = $script:result
        prev     = $script:prevResult
        error    = $script:errorText
        machine  = $env:COMPUTERNAME
        elevated = $elevated
        settings = $script:settings
    }
}

function Resolve-ReportPath {
    if (-not $script:result) { return $null }

    $path = $script:result.report
    if (-not $path) { return $null }
    if (-not [System.IO.Path]::IsPathRooted($path)) { $path = Join-Path $rootDir $path }
    if (Test-Path $path) { return $path }
    return $null
}

# --- tiny loopback HTTP server -------------------------------------------

function Send-Response($stream, [string]$status, [string]$contentType, [byte[]]$body) {
    $header = "HTTP/1.1 $status`r`n" +
              "Content-Type: $contentType`r`n" +
              "Content-Length: $($body.Length)`r`n" +
              "Cache-Control: no-store`r`n" +
              "Connection: close`r`n`r`n"
    $headerBytes = [System.Text.Encoding]::ASCII.GetBytes($header)
    $stream.Write($headerBytes, 0, $headerBytes.Length)
    if ($body -and $body.Length -gt 0) { $stream.Write($body, 0, $body.Length) }
    $stream.Flush()
}

function Send-Json($stream, $object) {
    $json = $object | ConvertTo-Json -Depth 8 -Compress
    Send-Response $stream '200 OK' 'application/json; charset=utf-8' ([System.Text.Encoding]::UTF8.GetBytes($json))
}

function Send-Text($stream, [string]$status, [string]$text) {
    Send-Response $stream $status 'text/plain; charset=utf-8' ([System.Text.Encoding]::UTF8.GetBytes($text))
}

function Open-Browser {
    if ($NoBrowser) { return }
    $edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
    if (-not (Test-Path $edge)) { $edge = 'C:\Program Files\Microsoft\Edge\Application\msedge.exe' }
    if (Test-Path $edge) {
        Start-Process -FilePath $edge -ArgumentList "--app=$url"
    } else {
        Start-Process $url
    }
}

# --- tray + window icon ----------------------------------------------------

$script:trayIcon        = $null
$script:trayTimer       = $null
$script:windowIconTries = 0
$script:windowIconSet   = $false
$script:windowIconError = ''
$script:quitting        = $false

function Ensure-User32 {
    if ('Wha.Native' -as [type]) { return }
    Add-Type -Namespace Wha -Name Native -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode)]
public static extern IntPtr LoadImage(IntPtr hInst, string path, uint type, int cx, int cy, uint flags);
[DllImport("user32.dll")]
public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
[DllImport("user32.dll", SetLastError = true)]
public static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
[DllImport("user32.dll")]
public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")]
public static extern bool ShowWindow(IntPtr hWnd, int cmd);
'@
}

function Resolve-IconPath {
    $local = Join-Path $guiDir 'logo.ico'
    if (Test-Path $local) { return $local }
    $parent = Join-Path $rootDir 'logo.ico'
    if (Test-Path $parent) { return $parent }
    return $null
}

function Find-AppWindow {
    foreach ($proc in @(Get-Process msedge -ErrorAction SilentlyContinue)) {
        if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { continue }
        if ($proc.MainWindowTitle -like '*WinHealthAudit*') { return $proc.MainWindowHandle }
    }
    return [IntPtr]::Zero
}

function Show-AppWindow {
    # Called from tray handlers: never goes through the HTTP server, because
    # the server thread is the one pumping these messages.
    try {
        $hwnd = Find-AppWindow
        if ($hwnd -ne [IntPtr]::Zero) {
            Ensure-User32
            [Wha.Native]::ShowWindow($hwnd, 9) | Out-Null
            [Wha.Native]::SetForegroundWindow($hwnd) | Out-Null
        } else {
            Open-Browser
        }
    } catch { Open-Browser }
}

function Set-WindowIcon {
    # Best effort: if the window or the icon cannot be found, the app simply
    # keeps the default Edge icon.
    if ($script:windowIconSet) { return $true }
    $script:windowIconTries++
    if ($script:windowIconTries -gt 60) { return $true }

    $iconPath = Resolve-IconPath
    if (-not $iconPath) { return $true }

    try {
        $target = Find-AppWindow
        if ($target -eq [IntPtr]::Zero) { return $false }

        Ensure-User32
        $handle = [Wha.Native]::LoadImage([IntPtr]::Zero, $iconPath, 1, 0, 0, 0x50)
        if ($handle -eq [IntPtr]::Zero) { throw 'LoadImage returned null' }

        [Wha.Native]::SendMessage($target, 0x80, [IntPtr]::Zero, $handle) | Out-Null
        [Wha.Native]::SendMessage($target, 0x80, [IntPtr]1, $handle) | Out-Null
        [Wha.Native]::SetClassLongPtr($target, -14, $handle) | Out-Null
        [Wha.Native]::SetClassLongPtr($target, -34, $handle) | Out-Null
        $script:windowIconSet = $true
        return $true
    } catch {
        $script:windowIconError = $_.Exception.ToString()
        return $true
    }
}

function Start-Tray {
    # Runs even with -NoBrowser: the launcher opens the window itself and the
    # server stays responsible for the tray icon.
    try {
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName System.Drawing

        $iconPath = Resolve-IconPath
        if (-not $iconPath) { return }

        $ru  = ($script:settings.lang -eq 'ru')
        $say = { param($ruText, $enText) if ($ru) { $ruText } else { $enText } }

        $menu = New-Object System.Windows.Forms.ContextMenuStrip
        $openItem = New-Object System.Windows.Forms.ToolStripMenuItem((& $say 'Открыть' 'Open'))
        $runItem  = New-Object System.Windows.Forms.ToolStripMenuItem((& $say 'Запустить проверку' 'Run audit'))
        $quitItem = New-Object System.Windows.Forms.ToolStripMenuItem((& $say 'Выход' 'Quit'))
        [void]$menu.Items.AddRange(@($openItem, $runItem, (New-Object System.Windows.Forms.ToolStripSeparator), $quitItem))

        $openItem.Add_Click({ Show-AppWindow })
        $runItem.Add_Click({ Start-Run $script:days })
        $quitItem.Add_Click({
            $script:quitting = $true
            try { if ($listener) { $listener.Stop() } } catch { }
            try {
                foreach ($proc in @(Get-Process msedge -ErrorAction SilentlyContinue)) {
                    if ($proc.MainWindowHandle -ne [IntPtr]::Zero -and $proc.MainWindowTitle -like '*WinHealthAudit*') {
                        $proc.CloseMainWindow() | Out-Null
                    }
                }
            } catch { }
        })

        $icon = New-Object System.Drawing.Icon($iconPath)
        $notify = New-Object System.Windows.Forms.NotifyIcon
        $notify.Icon = $icon
        $notify.Text = 'WinHealthAudit'
        $notify.ContextMenuStrip = $menu
        $notify.Visible = $true
        $notify.Add_MouseDoubleClick({ Show-AppWindow })

        $script:trayIcon  = $notify
        $script:trayTimer = New-Object System.Windows.Forms.Timer
        $script:trayTimer.Interval = 250
        $script:trayTimer.Add_Tick({ Set-WindowIcon | Out-Null })
        $script:trayTimer.Start()
    } catch {
        try { Add-Content -Path (Join-Path $settingsDir 'tray.log') -Value $_.Exception.ToString() } catch { }
    }
}

function Handle-Request($stream, [string]$method, [string]$target) {
    $path = $target
    $query = ''
    $question = $target.IndexOf('?')
    if ($question -ge 0) {
        $path  = $target.Substring(0, $question)
        $query = $target.Substring($question + 1)
    }

    switch ($path) {
        '/' { Send-Response $stream '200 OK' 'text/html; charset=utf-8' $html }

        '/logo.png' {
            $logo = Join-Path $guiDir 'logo.png'
            if (Test-Path $logo) {
                Send-Response $stream '200 OK' 'image/png' ([System.IO.File]::ReadAllBytes($logo))
            } else {
                Send-Text $stream '404 Not Found' 'no logo'
            }
        }

        '/api/state' { Send-Json $stream (Get-State) }

        '/api/tray' {
            Send-Json $stream ([pscustomobject]@{
                tray        = [bool]$script:trayIcon
                trayText    = if ($script:trayIcon) { $script:trayIcon.Text } else { '' }
                windowIcon  = [bool]$script:windowIconSet
                iconError   = [string]$script:windowIconError
                iconTries   = [int]$script:windowIconTries
            })
        }

        '/api/settings' {
            if ($query) { Write-Settings $query }
            Send-Json $stream $script:settings
        }

        '/api/hide' {
            $key = ''
            if ($query -match 'k=(.*)$') { $key = [Uri]::UnescapeDataString($Matches[1]) }
            if ($key) {
                $list = @()
                if ($script:settings.hidden) { $list = @($script:settings.hidden | Where-Object { $_ }) }
                if ($list -contains $key) {
                    $list = @($list | Where-Object { $_ -ne $key })
                } else {
                    $list += $key
                }
                $script:settings.hidden = $list
                Save-Settings
            }
            Send-Json $stream $script:settings
        }

        '/api/run' {
            $days = $script:days
            if ($query -match 'days=(\d+)') { $days = [Math]::Min(365, [Math]::Max(1, [int]$Matches[1])) }
            Start-Run $days
            Send-Json $stream ([pscustomobject]@{ ok = $true })
        }

        '/api/open' {
            $report = Resolve-ReportPath
            if ($report) {
                Start-Process -FilePath $report
                Send-Json $stream ([pscustomobject]@{ ok = $true })
            } else {
                Send-Json $stream ([pscustomobject]@{ ok = $false })
            }
        }

        '/api/open-folder' {
            $folder = Reports-Dir
            New-Item -ItemType Directory -Path $folder -Force | Out-Null
            Start-Process -FilePath $folder
            Send-Json $stream ([pscustomobject]@{ ok = $true })
        }

        '/api/report' {
            $report = Resolve-ReportPath
            if ($report) {
                Send-Response $stream '200 OK' 'text/markdown; charset=utf-8' ([System.IO.File]::ReadAllBytes($report))
            } else {
                Send-Text $stream '404 Not Found' 'no report yet - run an audit first'
            }
        }

        default { Send-Text $stream '404 Not Found' 'not found' }
    }
}

# --- main -----------------------------------------------------------------

$url = "http://127.0.0.1:$Port/"
function New-Listener($port) {
    $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $port)
    $l.Start()
    return $l
}

$listener = $null
try {
    $listener = New-Listener $Port
} catch {
    # Another instance may already serve this port - reuse it, unless we are
    # elevated and the old one is not: then take the port over.
    $reuse     = $false
    $takeOver  = $false

    try {
        $probe = Invoke-WebRequest "http://127.0.0.1:$Port/api/state" -UseBasicParsing -TimeoutSec 2
        if ($probe.StatusCode -eq 200) {
            $their = $null
            try { $their = $probe.Content | ConvertFrom-Json } catch { }
            if ((Test-Elevated) -and $their -and -not $their.elevated) {
                $takeOver = $true
            } else {
                $reuse = $true
            }
        }
    } catch { }

    if ($takeOver) {
        try {
            $conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
                    Select-Object -First 1
            if ($conn) { Stop-Process -Id $conn.OwningProcess -Force -ErrorAction SilentlyContinue }
        } catch { }
        Start-Sleep -Milliseconds 700
        try { $listener = New-Listener $Port } catch { }
    } elseif ($reuse) {
        Write-Host "WinHealthAudit is already running on port $Port"
        Open-Browser
        exit 0
    }
}

if (-not $listener) {
    Write-Host "port $Port is busy - stop the other window or run: app.ps1 -Port 8899"
    exit 1
}

Write-Host "WinHealthAudit \\ ESLL - window server on $url  (ctrl+c to stop)"
Open-Browser
Start-Tray

try {
    while ($true) {
        # Poll instead of a blocking accept: the gap is where the tray menu,
        # the icon timer and the quit flag get their turn on the same thread.
        while ($true) {
            if ($script:quitting) { break }
            $pending = $false
            try { $pending = $listener.Pending() } catch { break }
            if ($pending) { break }
            if ($script:trayTimer) { [System.Windows.Forms.Application]::DoEvents() }
            Start-Sleep -Milliseconds 80
        }
        if ($script:quitting) { break }

        $client = $listener.AcceptTcpClient()
        $reader = $null
        try {
            $stream = $client.GetStream()
            $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8, $false, 4096, $true)
            $requestLine = $reader.ReadLine()

            if ($requestLine) {
                $parts = $requestLine.Split(' ')
                while (($line = $reader.ReadLine()) -ne $null -and $line -ne '') { }
                if ($parts.Length -ge 2) { Handle-Request $stream $parts[0] $parts[1] }
            }
        } catch {
            try { Send-Text $stream '500 Internal Server Error' $_.Exception.Message } catch { }
        } finally {
            if ($reader) { $reader.Close() }
            $client.Close()
        }
    }
} catch {
    if (-not $script:quitting) { Write-Host "server stopped: $($_.Exception.Message)" }
} finally {
    if ($script:trayTimer) {
        try { $script:trayTimer.Stop() } catch { }
    }
    if ($script:trayIcon) {
        try { $script:trayIcon.Visible = $false; $script:trayIcon.Dispose() } catch { }
    }
    if ($script:job) {
        Stop-Job -Job $script:job -ErrorAction SilentlyContinue
        Remove-Job -Job $script:job -Force -ErrorAction SilentlyContinue
    }
    Get-Job -Name 'WinHealthAudit' -ErrorAction SilentlyContinue | Remove-Job -Force -ErrorAction SilentlyContinue
    $listener.Stop()
}
