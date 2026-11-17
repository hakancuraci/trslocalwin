$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'dist/BalbalTurkce'
function Run([scriptblock] $Command) { & $Command; if ($LASTEXITCODE -ne 0) { throw "Command failed: $Command" } }
if (!(Test-Path "$root/runtimes/win-x64/native/balbal_native.dll")) { Run { & "$PSScriptRoot/build-native.ps1" } }
Run { dotnet publish "$root/src/BalbalTurkce/BalbalTurkce.csproj" -c Release -r win-x64 --self-contained true -o $out }
Run { dotnet publish "$root/src/BalbalIzleyici/BalbalIzleyici.csproj" -c Release -r win-x64 --self-contained true -o "$root/dist/watcher" }
Copy-Item "$root/dist/watcher/*" $out -Recurse -Force
Copy-Item "$root/README.txt" $out
Copy-Item "$root/IzleyiciyiDurdur.cmd" $out
New-Item -ItemType Directory -Force "$out/models/balbal-v2-manas" | Out-Null
Write-Host "Hazır: $out — kendi models/balbal-v2-manas dosyalarınızı bu klasöre taşıyın."
