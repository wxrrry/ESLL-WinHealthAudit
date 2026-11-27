function Invoke-storageAction([string]$action, $argsObj) {
    switch ($action) {
        'drives' { return Get-StorageDrives }
        'scan' { return Scan-StoragePath ([string]$argsObj.path) }
        'duplicates' { return Find-Duplicates ([string]$argsObj.path) }
        'recycle' { return Send-ToRecycleBin ([string]$argsObj.path) }
        'reveal' {
            $p = [string]$argsObj.path
            if ($p -and (Test-Path -LiteralPath $p)) {
                Start-Process 'explorer.exe' -ArgumentList ('/select,"' + $p + '"')
                return [pscustomobject]@{ ok = $true }
            }
            return [pscustomobject]@{ ok = $false; error = 'Path not found' }
        }
        default { return [pscustomobject]@{ ok = $false; error = "Unknown action: $action" } }
    }
}

function Get-StorageDrives {
    $drives = @()
    Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3 OR DriveType=2" -ErrorAction SilentlyContinue | ForEach-Object {
        $tot = if ($_.Size) { [int64]$_.Size } else { 0 }
        $fre = if ($_.FreeSpace) { [int64]$_.FreeSpace } else { 0 }
        $drives += [pscustomobject]@{
            drive = $_.DeviceID
            root = $_.DeviceID + '\'
            label = $_.VolumeName
            fs = $_.FileSystem
            totalBytes = $tot
            freeBytes = $fre
            usedBytes = ($tot - $fre)
            usedPct = if ($tot -gt 0) { [math]::Round((($tot - $fre) / $tot) * 100) } else { 0 }
        }
    }
    return [pscustomobject]@{ ok = $true; items = $drives }
}

function Scan-StoragePath([string]$targetPath) {
    if (-not $targetPath) { $targetPath = 'C:\' }
    if (-not (Test-Path -LiteralPath $targetPath)) {
        return [pscustomobject]@{ ok = $false; error = 'Path does not exist' }
    }
    if (Get-Command Write-JobLog -ErrorAction SilentlyContinue) { Write-JobLog "Scanning $targetPath ..." }

    $extStats = @{}
    $topFiles = New-Object System.Collections.Generic.List[object]
    $folders = @()
    $totalBytes = [int64]0
    $fileCount = 0

    $entries = Get-ChildItem -LiteralPath $targetPath -Force -ErrorAction SilentlyContinue
    foreach ($entry in $entries) {
        if ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { continue }
        if ($entry.PSIsContainer) {
            $dirBytes = [int64]0
            $dirFiles = 0
            try {
                $stack = New-Object System.Collections.Generic.Stack[string]
                $stack.Push($entry.FullName)
                while ($stack.Count -gt 0) {
                    $cur = $stack.Pop()
                    $di = New-Object System.IO.DirectoryInfo($cur)
                    foreach ($f in $di.GetFiles()) {
                        $len = $f.Length
                        $dirBytes += $len
                        $dirFiles++
                        $fileCount++
                        $ext = if ($f.Extension) { $f.Extension.ToLowerInvariant() } else { '(none)' }
                        if (-not $extStats.ContainsKey($ext)) { $extStats[$ext] = [int64]0 }
                        $extStats[$ext] += $len
                        if ($len -ge 10485760) {
                            $topFiles.Add([pscustomobject]@{ name = $f.Name; path = $f.FullName; bytes = $len })
                        }
                    }
                    foreach ($sd in $di.GetDirectories()) {
                        if (-not ($sd.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
                            $stack.Push($sd.FullName)
                        }
                    }
                }
            } catch { }
            $totalBytes += $dirBytes
            $folders += [pscustomobject]@{
                name = $entry.Name
                path = $entry.FullName
                bytes = $dirBytes
                files = $dirFiles
                isDir = $true
            }
        } else {
            $len = $entry.Length
            $totalBytes += $len
            $fileCount++
            $ext = if ($entry.Extension) { $entry.Extension.ToLowerInvariant() } else { '(none)' }
            if (-not $extStats.ContainsKey($ext)) { $extStats[$ext] = [int64]0 }
            $extStats[$ext] += $len
            if ($len -ge 10485760) {
                $topFiles.Add([pscustomobject]@{ name = $entry.Name; path = $entry.FullName; bytes = $len })
            }
            $folders += [pscustomobject]@{
                name = $entry.Name
                path = $entry.FullName
                bytes = $len
                files = 1
                isDir = $false
            }
        }
    }

    $folders = @($folders | Sort-Object bytes -Descending)
    $largest = @($topFiles | Sort-Object bytes -Descending | Select-Object -First 40)
    $types = @($extStats.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 20 | ForEach-Object {
        [pscustomobject]@{ ext = $_.Key; bytes = $_.Value }
    })

    if (Get-Command Write-JobLog -ErrorAction SilentlyContinue) { Write-JobLog ("Done: $fileCount files, " + [math]::Round($totalBytes / 1GB, 2) + " GB") }
    return [pscustomobject]@{
        ok = $true
        path = $targetPath
        totalBytes = $totalBytes
        fileCount = $fileCount
        folders = $folders
        largest = $largest
        types = $types
    }
}

function Find-Duplicates([string]$targetPath) {
    if (-not $targetPath -or -not (Test-Path -LiteralPath $targetPath)) {
        return [pscustomobject]@{ ok = $false; error = 'Invalid path' }
    }
    if (Get-Command Write-JobLog -ErrorAction SilentlyContinue) { Write-JobLog "Indexing files for duplicates in $targetPath..." }
    $bySize = @{}
    Get-ChildItem -LiteralPath $targetPath -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Length -ge 4096 } | ForEach-Object {
        $sz = [string]$_.Length
        if (-not $bySize.ContainsKey($sz)) { $bySize[$sz] = New-Object System.Collections.Generic.List[string] }
        $bySize[$sz].Add($_.FullName)
    }

    $dups = @()
    foreach ($kv in $bySize.GetEnumerator()) {
        if ($kv.Value.Count -lt 2) { continue }
        $byHash = @{}
        foreach ($fp in $kv.Value) {
            try {
                $h = (Get-FileHash -LiteralPath $fp -Algorithm SHA256 -ErrorAction Stop).Hash
                if (-not $byHash.ContainsKey($h)) { $byHash[$h] = New-Object System.Collections.Generic.List[string] }
                $byHash[$h].Add($fp)
            } catch { }
        }
        foreach ($hk in $byHash.GetEnumerator()) {
            if ($hk.Value.Count -gt 1) {
                $dups += [pscustomobject]@{
                    hash = $hk.Key.Substring(0, 12)
                    bytes = [int64]$kv.Key
                    count = $hk.Value.Count
                    files = @($hk.Value)
                }
            }
        }
        if ($dups.Count -ge 100) { break }
    }
    return [pscustomobject]@{ ok = $true; groups = @($dups | Sort-Object bytes -Descending) }
}

function Send-ToRecycleBin([string]$p) {
    if (-not $p -or -not (Test-Path -LiteralPath $p)) {
        return [pscustomobject]@{ ok = $false; error = 'Path not found' }
    }
    Add-Type -AssemblyName Microsoft.VisualBasic
    if (Test-Path -LiteralPath $p -PathType Container) {
        [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($p, 'OnlyErrorDialogs', 'SendToRecycleBin')
    } else {
        [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($p, 'OnlyErrorDialogs', 'SendToRecycleBin')
    }
    return [pscustomobject]@{ ok = $true }
}
