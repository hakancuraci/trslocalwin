#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$root/.deps"
if [ ! -d "$root/.deps/kenlm/.git" ]; then git clone https://github.com/kpu/kenlm.git "$root/.deps/kenlm"; fi
git -C "$root/.deps/kenlm" checkout --detach 4cb443e60b7bf2c0ddf3c745378f76cb59e254e5
if [ ! -d "$root/.deps/sentencepiece/.git" ]; then git clone https://github.com/google/sentencepiece.git "$root/.deps/sentencepiece"; fi
git -C "$root/.deps/sentencepiece" checkout --detach 31646a467d2051eb904e0b45de3a73e91fe1c1e3
cmake -S "$root/native" -B "$root/native/build" -DCMAKE_BUILD_TYPE=Release -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DKENLM_ROOT="$root/.deps/kenlm" -DSENTENCEPIECE_ROOT="$root/.deps/sentencepiece"
cmake --build "$root/native/build" --target balbal_native --parallel 4
mkdir -p "$root/runtimes/linux-x64/native"
cp "$root/native/build/libbalbal_native.so" "$root/runtimes/linux-x64/native/"
