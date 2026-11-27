function Invoke-benchmarkAction([string]$action, $argsObj) {
    switch ($action) {
        'cpu' { return Run-CpuBenchmark }
        'memory' { return Run-MemoryBenchmark }
        'disk' { return Run-DiskBenchmark ([string]$argsObj.drive) }
        'history' { return Get-BenchmarkHistory }
        'save' { return Save-BenchmarkRun $argsObj }
        default { return [pscustomobject]@{ ok = $false; error = "Unknown action: $action" } }
    }
}

function Run-CpuBenchmark {
    $threads = [Environment]::ProcessorCount
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $limit = 120000
    $primes = 0
    for ($n = 2; $n -le $limit; $n++) {
        $isPrime = $true
        $max = [int][Math]::Sqrt($n)
        for ($d = 2; $d -le $max; $d++) {
            if (($n % $d) -eq 0) { $isPrime = $false; break }
        }
        if ($isPrime) { $primes++ }
    }
    $sw.Stop()
    $ms = [math]::Max(1, $sw.Elapsed.TotalMilliseconds)
    $singleScore = [math]::Round((3000000.0 / $ms))
    $multiScore = [math]::Round($singleScore * $threads * 0.78)
    return [pscustomobject]@{
        ok = $true
        threads = $threads
        elapsedMs = [math]::Round($ms, 1)
        primes = $primes
        singleScore = $singleScore
        multiScore = $multiScore
    }
}

function Run-MemoryBenchmark {
    $sizeBytes = 64 * 1024 * 1024
    $src = New-Object byte[] $sizeBytes
    $dst = New-Object byte[] $sizeBytes
    (New-Object Random).NextBytes($src)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt 16; $i++) {
        [Buffer]::BlockCopy($src, 0, $dst, 0, $sizeBytes)
    }
    $sw.Stop()
    $totalMB = (64 * 16)
    $mbps = [math]::Round($totalMB / [math]::Max(0.001, $sw.Elapsed.TotalSeconds))
    $score = [math]::Round($mbps / 4.0)
    return [pscustomobject]@{
        ok = $true
        bandwidthMBs = $mbps
        elapsedMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 1)
        score = $score
    }
}

function Run-DiskBenchmark([string]$drive) {
    if (-not $drive) { $drive = 'C:' }
    $dir = Join-Path ($drive.TrimEnd('\') + '\') 'WinHealthAudit_Bench_Tmp'
    $file = Join-Path $dir 'bench.bin'
    $sizeMB = 128
    $chunk = New-Object byte[] (4 * 1024 * 1024)
    (New-Object Random).NextBytes($chunk)

    try {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $swW = [System.Diagnostics.Stopwatch]::StartNew()
        $fs = [System.IO.File]::Open($file, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        for ($i = 0; $i -lt ($sizeMB / 4); $i++) {
            $fs.Write($chunk, 0, $chunk.Length)
        }
        $fs.Flush($true)
        $fs.Close()
        $swW.Stop()

        $swR = [System.Diagnostics.Stopwatch]::StartNew()
        $fs2 = [System.IO.File]::OpenRead($file)
        $buf = New-Object byte[] (4 * 1024 * 1024)
        while ($fs2.Read($buf, 0, $buf.Length) -gt 0) { }
        $fs2.Close()
        $swR.Stop()

        $writeMBs = [math]::Round($sizeMB / [math]::Max(0.001, $swW.Elapsed.TotalSeconds), 1)
        $readMBs  = [math]::Round($sizeMB / [math]::Max(0.001, $swR.Elapsed.TotalSeconds), 1)
        $score = [math]::Round(($writeMBs + $readMBs) * 1.5)
        return [pscustomobject]@{
            ok = $true
            drive = $drive
            writeMBs = $writeMBs
            readMBs = $readMBs
            score = $score
        }
    } finally {
        if (Test-Path -LiteralPath $dir) {
            Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-BenchmarkHistory {
    $file = Join-Path $script:WhaSettingsDir 'benchmarks.json'
    if (-not (Test-Path -LiteralPath $file)) { return [pscustomobject]@{ ok = $true; items = @() } }
    try {
        $items = @(Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json)
        return [pscustomobject]@{ ok = $true; items = $items }
    } catch {
        return [pscustomobject]@{ ok = $true; items = @() }
    }
}

function Save-BenchmarkRun($argsObj) {
    $file = Join-Path $script:WhaSettingsDir 'benchmarks.json'
    $list = @()
    if (Test-Path -LiteralPath $file) {
        try { $list = @(Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { }
    }
    $entry = [pscustomobject]@{
        at = (Get-Date).ToString('yyyy-MM-dd HH:mm')
        cpu = $argsObj.cpu
        memory = $argsObj.memory
        disk = $argsObj.disk
        gpu = $argsObj.gpu
        overall = $argsObj.overall
    }
    $list = @($entry) + $list | Select-Object -First 30
    New-Item -ItemType Directory -Path $script:WhaSettingsDir -Force | Out-Null
    $list | ConvertTo-Json -Depth 5 | Set-Content $file -Encoding UTF8
    return [pscustomobject]@{ ok = $true; items = $list }
}
