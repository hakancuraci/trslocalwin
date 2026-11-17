$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Run([scriptblock] $Command) { & $Command; if ($LASTEXITCODE -ne 0) { throw "Native build command failed: $Command" } }
New-Item -ItemType Directory -Force "$root/.deps" | Out-Null
if (!(Test-Path "$root/.deps/kenlm/.git")) { Run { git clone https://github.com/kpu/kenlm.git "$root/.deps/kenlm" } }
Run { git -C "$root/.deps/kenlm" checkout --detach 4cb443e60b7bf2c0ddf3c745378f76cb59e254e5 }
if (!(Test-Path "$root/.deps/sentencepiece/.git")) { Run { git clone https://github.com/google/sentencepiece.git "$root/.deps/sentencepiece" } }
Run { git -C "$root/.deps/sentencepiece" checkout --detach 31646a467d2051eb904e0b45de3a73e91fe1c1e3 }
Run { cmake -S "$root/native" -B "$root/native/build" -A x64 -DCMAKE_POLICY_VERSION_MINIMUM=3.5 "-DKENLM_ROOT=$root/.deps/kenlm" "-DSENTENCEPIECE_ROOT=$root/.deps/sentencepiece" }
Run { cmake --build "$root/native/build" --config Release --target balbal_native --parallel 4 }
New-Item -ItemType Directory -Force "$root/runtimes/win-x64/native" | Out-Null
Copy-Item "$root/native/build/Release/balbal_native.dll" "$root/runtimes/win-x64/native/"
