param([switch]$TestModel)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'dist/BalbalTurkce'
$report = [ordered]@{ status = 'running'; os = [Environment]::OSVersion.ToString(); checks = @(); model = 'not requested' }
function Check([bool]$Ok, [string]$Name) {
    if (!$Ok) { throw "FAIL: $Name" }
    $script:report.checks += $Name
    Write-Host "PASS: $Name"
}
function Worker([string[]]$Arguments, [int]$Timeout = 600) {
    $id = [guid]::NewGuid().ToString('N')
    $stdout = Join-Path $app "$id.stdout.log"
    $stderr = Join-Path $app "$id.stderr.log"
    $process = Start-Process -FilePath "$app/BalbalTurkce.exe" -ArgumentList $Arguments -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    try {
        if (!$process.WaitForExit($Timeout * 1000)) { throw 'Worker timed out' }
        $process.Refresh()
        $lines = @(Get-Content -LiteralPath $stdout | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
        return [pscustomobject]@{ Code = $process.ExitCode; Events = $lines; Error = (Get-Content -Raw -LiteralPath $stderr) }
    } finally { if (!$process.HasExited) { Stop-Process -Id $process.Id -Force } }
}
function Window([string]$Executable, [switch]$RequireWorker) {
    $process = Start-Process -FilePath (Join-Path $app $Executable) -PassThru
    try {
        $until = [DateTime]::UtcNow.AddSeconds(30)
        do {
            Start-Sleep -Milliseconds 200
            $process.Refresh()
            if ($process.HasExited) { throw "$Executable exited before opening a window" }
        } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $until)
        Check ($process.MainWindowHandle -ne 0) "$Executable creates a Windows window"
        $descendants = @(Get-CimInstance Win32_Process | Where-Object { $_.ParentProcessId -eq $process.Id } | Select-Object -ExpandProperty ProcessId)
        if ($RequireWorker) {
            $until = [DateTime]::UtcNow.AddSeconds(10)
            while ($descendants.Count -eq 0 -and [DateTime]::UtcNow -lt $until) {
                Start-Sleep -Milliseconds 100
                $descendants = @(Get-CimInstance Win32_Process | Where-Object { $_.ParentProcessId -eq $process.Id } | Select-Object -ExpandProperty ProcessId)
            }
            Check ($descendants.Count -gt 0) "$Executable has a worker before close"
        }
        [void]$process.CloseMainWindow()
        Check ($process.WaitForExit(10000)) "$Executable closes normally"
        foreach ($childId in $descendants) {
            $until = [DateTime]::UtcNow.AddSeconds(5)
            while ((Get-Process -Id $childId -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $until) { Start-Sleep -Milliseconds 100 }
            Check (!(Get-Process -Id $childId -ErrorAction SilentlyContinue)) "$Executable terminates worker $childId on close"
        }
    } finally { if (!$process.HasExited) { Stop-Process -Id $process.Id -Force } }
}
try {
    Check (Test-Path "$app/balbal_native.dll") 'Windows decoder DLL bundled'
    & "$app/tools/ffmpeg/ffmpeg.exe" -version | Select-Object -First 1
    Check ($LASTEXITCODE -eq 0) 'Bundled FFmpeg executes on Windows'
    # This must report missing model, not a native DLL/runtime loader failure.
    $missing = Worker -Arguments @('--worker', '--job', 'model')
    Check ($missing.Code -ne 0 -and @($missing.Events | Where-Object { $_.kind -eq 'fatal' -and $_.message -match 'Yerel model eksik' }).Count -gt 0) 'Missing local model handled without a crash'
    Window 'BalbalTurkce.exe'
    Window 'BalbalIzleyici.exe'
    if ($TestModel) {
        $revision = 'c651a680a9147b0a7eb3fd7f5f4f914f3f4ddc25'
        $model = Join-Path $app 'models/balbal-v2-manas'
        foreach ($file in @('model.json','lm/model.binary','lm/tokenizer.model','onnx/model.onnx','onnx/model.onnx.data')) {
            $target = Join-Path $model $file
            New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
            Invoke-WebRequest "https://huggingface.co/erayyapagci/balbal-v2-manas/resolve/$revision/$file" -OutFile $target
        }
        $audio = Join-Path $app 'windows-test.mp3'
        Invoke-WebRequest 'https://raw.githubusercontent.com/hakancuraci/trslocal/99a46ae3fc7e8b8a6eea596a815834f08c7e47bf/first-test2.mp3' -OutFile $audio
        $result = Worker -Arguments @('--worker','--job','transcribe','--file',('"' + $audio + '"'))
        Check ($result.Code -eq 0) 'Windows native model/tokenizer/KenLM load and inference'
        $text = @($result.Events | Where-Object { $_.kind -eq 'text' } | Select-Object -ExpandProperty message)
        Check ($text.Count -eq 1 -and ![string]::IsNullOrWhiteSpace($text[0])) 'Real MP3 produces transcript on Windows'
        $expected = Get-Content -Raw -Encoding utf8 (Join-Path $root 'tests/fixtures/manas-reference.txt')
        Check ($text[0].Trim() -ceq $expected.Trim()) 'Windows transcript equals Python Manas reference'
        $report.model = 'passed: real MP3, pinned Manas fast graph'
        # Close the dashboard while its model worker is active.
        Window 'BalbalIzleyici.exe' -RequireWorker
        Remove-Item -Recurse -Force $model
        Remove-Item $audio
    }
    $report.status = 'passed'
} catch {
    $report.status = 'failed'
    $report.error = $_.Exception.Message
    throw
} finally {
    if ($TestModel -and (Test-Path "$app/models/balbal-v2-manas/model.json")) {
        Remove-Item -Recurse -Force "$app/models/balbal-v2-manas"
    }
    $report | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $root 'dist/windows-validation.json')
}
